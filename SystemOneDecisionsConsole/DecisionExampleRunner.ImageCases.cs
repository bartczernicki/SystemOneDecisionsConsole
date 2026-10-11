using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SystemOneDecisionsConsole.Models;

namespace SystemOneDecisionsConsole;

internal sealed partial class DecisionExampleRunner
{
    private static void ValidateImageCasesMetadata(DecisionExample example)
    {
        IReadOnlyList<DecisionImageCase> cases = example.ImageCases!;
        if (example.Backend != DecisionBackend.LocalDecisions || example.QuestionType != "choice" ||
            example.Aggregation is not null || example.EvidenceStages is not null || example.Frames is not null ||
            example.Evaluation is not null || example.Tasks is not null || example.ExplorationExploitation is not null ||
            cases.Count != 2 || cases.Any(item => item is null || string.IsNullOrWhiteSpace(item.Label) ||
                string.IsNullOrWhiteSpace(item.ImageFile) || Path.GetFileName(item.ImageFile) != item.ImageFile ||
                !item.ImageFile.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) ||
            cases.Select(item => item.Label).Distinct(StringComparer.Ordinal).Count() != cases.Count ||
            cases.Select(item => item.ImageFile).Distinct(StringComparer.OrdinalIgnoreCase).Count() != cases.Count)
        {
            throw new InvalidDataException("Image cases require two distinct named PNG images in a Local Decisions choice example without other specialized metadata.");
        }
    }

    private async Task RunImageCasesAsync(DecisionExample example, string folder, Uri endpoint, string model)
    {
        string input = await ReadTextAsync(folder, "input.txt");
        string questionsJson = await ReadTextAsync(folder, "questions.json");
        RejectDuplicateJsonProperties(questionsJson);
        var questions = Deserialize<List<DecisionQuestion>>(questionsJson);
        ValidateQuestions(questions, "choice");
        ValidateImageCaseQuestions(questions);

        // Validate both images before sending either independent package request.
        IReadOnlyList<DecisionImageCase> cases = example.ImageCases!;
        var imageUrls = new List<string>();
        foreach (DecisionImageCase imageCase in cases)
        {
            byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(folder, imageCase.ImageFile));
            try
            {
                ValidatePng(bytes);
            }
            catch (InvalidDataException error)
            {
                throw new InvalidDataException($"{imageCase.ImageFile}: invalid PNG image. {error.Message}", error);
            }
            imageUrls.Add($"data:image/png;base64,{Convert.ToBase64String(bytes)}");
        }

        DisplayTextSection("Input:", input, ConsoleColor.Green, "\u001b[38;2;128;255;160m");
        for (int index = 0; index < cases.Count; index++)
        {
            DecisionImageCase imageCase = cases[index];
            Console.WriteLine();
            DisplayColoredLine($"Image case {index + 1} of {cases.Count}: {imageCase.Label}",
                ConsoleColor.Magenta, "\u001b[38;2;255;128;224m");
            Console.WriteLine($"Image: {imageCase.ImageFile}");
            var capture = new RequestCapture();
            try
            {
                var request = new DecisionsMultimodalRequest
                {
                    Model = model,
                    Input = [new DecisionInputMessage
                    {
                        Content = [new DecisionInputText { Text = $"Package: {imageCase.Label}\n\n{input}" },
                            new DecisionInputImage { ImageUrl = imageUrls[index] }]
                    }],
                    Questions = questions
                };
                string body = await SendAsync(endpoint, request, example.Backend, apiKey: null, capture,
                    responseSanitizer: body => RedactImageData(body, imageUrls));
                RejectDuplicateJsonProperties(body);
                var response = Deserialize<DecisionsResponse>(body);
                ValidateImageCaseAnswers(response, questions);
                for (int questionIndex = 0; questionIndex < questions.Count; questionIndex++)
                {
                    if (questionIndex > 0)
                    {
                        Console.WriteLine();
                    }
                    DisplayDecisionAnswer(response.Answers[questionIndex], "choice", questionIndex + 1);
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"Error: {imageCase.Label}: API request timed out after 60 seconds.");
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
                JsonException or HttpRequestException or NotSupportedException or FormatException)
            {
                Console.WriteLine($"Error: {imageCase.Label}: {RedactImageData(error.Message, imageUrls)}");
            }
            if (capture.Body is not null)
            {
                DisplayResponseJson(capture.Body);
            }
        }
    }

    private static void ValidateImageCaseQuestions(List<DecisionQuestion> questions)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (DecisionQuestion definition in questions)
        {
            if (definition is not DecisionChoiceQuestion question || string.IsNullOrWhiteSpace(question.Name) ||
                !names.Add(question.Name))
            {
                throw new InvalidDataException("Image questions must be choice questions with unique nonblank names.");
            }
            var values = new HashSet<string>(StringComparer.Ordinal);
            foreach (DecisionChoiceOption choice in question.Choices)
            {
                if (choice.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(choice.Value.GetString()) ||
                    !values.Add(choice.Value.GetString()!) || string.IsNullOrWhiteSpace(choice.Description))
                {
                    throw new InvalidDataException($"{question.Name}: choices must have unique nonblank string values and descriptions.");
                }
            }
        }
    }

    private static void ValidateImageCaseAnswers(DecisionsResponse response, List<DecisionQuestion> questions)
    {
        if (string.IsNullOrWhiteSpace(response.Model) || response.Answers is null || response.Answers.Count != questions.Count)
        {
            throw new InvalidDataException("Malformed response: answers must match the number of questions.");
        }
        for (int index = 0; index < questions.Count; index++)
        {
            DecisionAnswer answer = response.Answers[index];
            var question = (DecisionChoiceQuestion)questions[index];
            if (answer is null || answer.Name != question.Name)
            {
                throw new InvalidDataException("Malformed response: answer names must match the requested questions in order.");
            }
            if (answer is DecisionRefusalAnswer)
            {
                continue;
            }
            if (answer is not DecisionChoiceAnswer choice || choice.Choice.ValueKind != JsonValueKind.String ||
                !double.IsFinite(choice.Confidence) || choice.Confidence < 0 || choice.Confidence > 1 ||
                choice.Probabilities is null || choice.Probabilities.Count != question.Choices.Count)
            {
                throw new InvalidDataException($"Malformed response: expected a complete choice answer for {question.Name}.");
            }
            var allowed = question.Choices.Select(option => option.Value.GetString()!).ToHashSet(StringComparer.Ordinal);
            var returned = new HashSet<string>(StringComparer.Ordinal);
            if (!allowed.Contains(choice.Choice.GetString()!))
            {
                throw new InvalidDataException($"Malformed response: selected choice for {question.Name} must be an allowed value.");
            }
            foreach (DecisionChoiceProbability probability in choice.Probabilities)
            {
                if (probability is null || probability.Value.ValueKind != JsonValueKind.String ||
                    !allowed.Contains(probability.Value.GetString()!) || !returned.Add(probability.Value.GetString()!) ||
                    !double.IsFinite(probability.Probability) || probability.Probability < 0 || probability.Probability > 1)
                {
                    throw new InvalidDataException($"Malformed response: {question.Name} probabilities must cover each allowed value once with finite values between 0 and 1.");
                }
            }
            if (Math.Abs(choice.Probabilities.Sum(item => item.Probability) - 1) > 1e-6)
            {
                throw new InvalidDataException($"Malformed response: {question.Name} probabilities must sum to 1 within 1e-6.");
            }
        }
    }

    private static string RedactImageData(string text, IReadOnlyList<string> imageUrls)
    {
        try
        {
            // Decode JSON strings before redacting, including escaped slashes or Unicode escapes in echoes.
            using var document = JsonDocument.Parse(text);
            using var stream = new MemoryStream();
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = JsonOptions.WriteIndented });
            void WriteRedactedJson(JsonElement value)
            {
                if (value.ValueKind == JsonValueKind.Object)
                {
                    writer.WriteStartObject();
                    foreach (JsonProperty property in value.EnumerateObject())
                    {
                        writer.WritePropertyName(RedactImageDataText(property.Name, imageUrls));
                        WriteRedactedJson(property.Value);
                    }
                    writer.WriteEndObject();
                }
                else if (value.ValueKind == JsonValueKind.Array)
                {
                    writer.WriteStartArray();
                    foreach (JsonElement item in value.EnumerateArray())
                    {
                        WriteRedactedJson(item);
                    }
                    writer.WriteEndArray();
                }
                else if (value.ValueKind == JsonValueKind.String)
                {
                    writer.WriteStringValue(RedactImageDataText(value.GetString()!, imageUrls));
                }
                else
                {
                    value.WriteTo(writer);
                }
            }
            WriteRedactedJson(document.RootElement);
            writer.Flush();
            text = Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            // Malformed or plain-text errors still receive text redaction below.
        }
        return RedactImageDataText(text, imageUrls);
    }

    private static string RedactImageDataText(string text, IReadOnlyList<string> imageUrls)
    {
        const string replacement = "[image data omitted]";
        foreach (string imageUrl in imageUrls)
        {
            int base64Offset = imageUrl.IndexOf(',') + 1;
            if (text.Length < imageUrl.Length - base64Offset)
            {
                continue;
            }
            string base64 = imageUrl[base64Offset..];
            text = text.Replace(imageUrl, replacement, StringComparison.Ordinal)
                .Replace(base64, replacement, StringComparison.Ordinal)
                .Replace(base64.Replace("/", "\\/", StringComparison.Ordinal), replacement, StringComparison.Ordinal)
                .Replace(JsonSerializer.Serialize(imageUrl, JsonOptions)[1..^1], replacement, StringComparison.Ordinal)
                .Replace(JsonSerializer.Serialize(base64, JsonOptions)[1..^1], replacement, StringComparison.Ordinal)
                .Replace(JsonSerializer.Serialize(base64, JsonOptions)[1..^1].Replace("/", "\\/", StringComparison.Ordinal),
                    replacement, StringComparison.Ordinal);
        }
        return Regex.Replace(text, @"data:image(?:/|\\/)[A-Za-z0-9.+-]+;base64,[A-Za-z0-9+/=\\]+",
            replacement, RegexOptions.IgnoreCase);
    }

    private static void ValidatePng(byte[] bytes)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (bytes.Length < 8 || !bytes.AsSpan(0, 8).SequenceEqual(signature))
        {
            throw new InvalidDataException("The PNG signature is missing.");
        }
        uint width = 0, height = 0;
        byte depth = 0, color = 0, interlace = 0;
        bool header = false, palette = false, imageData = false, dataEnded = false, end = false;
        using var compressed = new MemoryStream();
        int offset = 8;
        while (offset < bytes.Length)
        {
            if (bytes.Length - offset < 12)
            {
                throw new InvalidDataException("A PNG chunk is truncated.");
            }
            uint chunkLength = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
            if (chunkLength > int.MaxValue || chunkLength > bytes.Length - offset - 12)
            {
                throw new InvalidDataException("A PNG chunk has an invalid length.");
            }
            int length = (int)chunkLength;
            ReadOnlySpan<byte> typeBytes = bytes.AsSpan(offset + 4, 4);
            for (int index = 0; index < typeBytes.Length; index++)
            {
                if (typeBytes[index] is not (>= 65 and <= 90) and not (>= 97 and <= 122))
                {
                    throw new InvalidDataException("A PNG chunk type is invalid.");
                }
            }
            if ((typeBytes[2] & 32) != 0)
            {
                throw new InvalidDataException("A PNG chunk uses an invalid reserved type bit.");
            }
            uint expectedCrc = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 8 + length, 4));
            if (PngCrc(bytes.AsSpan(offset + 4, length + 4)) != expectedCrc)
            {
                throw new InvalidDataException("A PNG chunk checksum is invalid.");
            }
            string type = Encoding.ASCII.GetString(typeBytes);
            ReadOnlySpan<byte> data = bytes.AsSpan(offset + 8, length);
            if (!header && type != "IHDR")
            {
                throw new InvalidDataException("IHDR must be the first PNG chunk.");
            }
            if (imageData && type != "IDAT")
            {
                dataEnded = true;
            }
            switch (type)
            {
                case "IHDR":
                    if (header || length != 13)
                    {
                        throw new InvalidDataException("The PNG header is invalid.");
                    }
                    width = BinaryPrimitives.ReadUInt32BigEndian(data[..4]);
                    height = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(4, 4));
                    depth = data[8];
                    color = data[9];
                    interlace = data[12];
                    bool validDepth = color switch
                    {
                        0 => depth is 1 or 2 or 4 or 8 or 16,
                        2 or 4 or 6 => depth is 8 or 16,
                        3 => depth is 1 or 2 or 4 or 8,
                        _ => false
                    };
                    if (width == 0 || height == 0 || width > int.MaxValue || height > int.MaxValue ||
                        !validDepth || data[10] != 0 || data[11] != 0 || interlace > 1)
                    {
                        throw new InvalidDataException("PNG dimensions or encoding settings are invalid.");
                    }
                    header = true;
                    break;
                case "PLTE":
                    if (palette || imageData || color is 0 or 4 || length == 0 || length % 3 != 0 || length > 768 ||
                        color == 3 && length / 3 > 1 << depth)
                    {
                        throw new InvalidDataException("The PNG palette is invalid.");
                    }
                    palette = true;
                    break;
                case "IDAT":
                    if (dataEnded || color == 3 && !palette)
                    {
                        throw new InvalidDataException("PNG image data chunks or palette order are invalid.");
                    }
                    compressed.Write(data);
                    imageData = true;
                    break;
                case "IEND":
                    if (length != 0 || !imageData || offset + 12 != bytes.Length)
                    {
                        throw new InvalidDataException("The PNG end chunk is invalid.");
                    }
                    end = true;
                    break;
                default:
                    if ((typeBytes[0] & 32) == 0)
                    {
                        throw new InvalidDataException("An unsupported critical PNG chunk was found.");
                    }
                    break;
            }
            offset += length + 12;
        }
        if (!end || compressed.Length == 0)
        {
            throw new InvalidDataException("PNG image data or its end chunk is missing.");
        }

        ReadOnlySpan<byte> zlibBytes = compressed.GetBuffer().AsSpan(0, (int)compressed.Length);
        if (zlibBytes.Length < 6 || (zlibBytes[0] & 15) != 8 || zlibBytes[0] >> 4 > 7 ||
            ((zlibBytes[0] << 8) | zlibBytes[1]) % 31 != 0 || (zlibBytes[1] & 32) != 0)
        {
            throw new InvalidDataException("The PNG compressed data header is invalid.");
        }
        uint expectedAdler = BinaryPrimitives.ReadUInt32BigEndian(zlibBytes[^4..]);
        uint adler = 1;

        int channels = color switch { 0 or 3 => 1, 2 => 3, 4 => 2, _ => 4 };
        compressed.Position = 0;
        using var decoded = new ZLibStream(compressed, CompressionMode.Decompress);
        byte[] buffer = new byte[8192];
        if (interlace == 0)
        {
            ValidatePngRows(decoded, width, height, channels * depth, buffer, ref adler);
        }
        else
        {
            int[] startsX = [0, 4, 0, 2, 0, 1, 0], startsY = [0, 0, 4, 0, 2, 0, 1];
            int[] stepsX = [8, 8, 4, 4, 2, 2, 1], stepsY = [8, 8, 8, 4, 4, 2, 2];
            for (int pass = 0; pass < 7; pass++)
            {
                long passWidth = width <= startsX[pass] ? 0 : ((long)width - startsX[pass] + stepsX[pass] - 1) / stepsX[pass];
                long passHeight = height <= startsY[pass] ? 0 : ((long)height - startsY[pass] + stepsY[pass] - 1) / stepsY[pass];
                if (passWidth > 0 && passHeight > 0)
                {
                    ValidatePngRows(decoded, passWidth, passHeight, channels * depth, buffer, ref adler);
                }
            }
        }
        if (decoded.ReadByte() != -1)
        {
            throw new InvalidDataException("PNG image data has an unexpected decoded length.");
        }
        if (adler != expectedAdler)
        {
            throw new InvalidDataException("The PNG image data checksum is invalid.");
        }
    }

    private static void ValidatePngRows(Stream decoded, long width, long height, int bitsPerPixel, byte[] buffer,
        ref uint adler)
    {
        long rowBytes = (width * bitsPerPixel + 7) / 8;
        for (long row = 0; row < height; row++)
        {
            int filter = decoded.ReadByte();
            if (filter is < 0 or > 4)
            {
                throw new InvalidDataException("PNG image data is truncated or has an invalid row filter.");
            }
            adler = PngAdler(adler, [(byte)filter]);
            long remaining = rowBytes;
            while (remaining > 0)
            {
                int count = decoded.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (count == 0)
                {
                    throw new InvalidDataException("PNG image data is truncated.");
                }
                adler = PngAdler(adler, buffer.AsSpan(0, count));
                remaining -= count;
            }
        }
    }

    private static uint PngAdler(uint adler, ReadOnlySpan<byte> data)
    {
        uint first = adler & 65535, second = adler >> 16;
        foreach (byte value in data)
        {
            first = (first + value) % 65521;
            second = (second + first) % 65521;
        }
        return (second << 16) | first;
    }

    private static uint PngCrc(ReadOnlySpan<byte> data)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u);
            }
        }
        return ~crc;
    }
}
