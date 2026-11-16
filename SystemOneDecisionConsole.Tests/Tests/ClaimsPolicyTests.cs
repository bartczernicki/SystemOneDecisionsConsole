using System.Globalization;
using SystemOneDecisionsConsole;
using SystemOneDecisionsConsole.Models;

namespace SystemOneDecisionConsole.Tests;

[TestClass]
public sealed class ClaimsPolicyTests
{
    private static readonly string[] IntakeChoices = ["New Claim Notification", "Document Submission", "Status Inquiry", "Complaint", "Unrelated Correspondence"];
    private static readonly string[] ValidationChoices = ["Continue Workflow", "Request Additional Information", "Duplicate Review", "Specialist Review"];
    private static ClaimsPolicy Policy(ClaimsWorkflow workflow) => new(.90, .90,
        workflow == ClaimsWorkflow.Validation ? 5000m : null);

    private static ClaimsCase CompleteCase() => new("TEST-01", "Fictional property claim", "A fictional policyholder reports water damage.", [],
        new("Alex Rivera", "alex@example.invalid", "POL-100", "2026-11-14", "Water damaged a kitchen floor.", "CLM-100", 2500m, "USD"),
        new(false, false, false, false, true, false));

    private static SystemOneChoiceAnswer Answer(ClaimsWorkflow workflow, string? selected = null,
        double probability = .94, double confidence = .94)
    {
        string[] choices = workflow == ClaimsWorkflow.EmailIntake ? IntakeChoices : ValidationChoices;
        selected ??= choices[0];
        return new() { Choice = selected, Confidence = confidence,
            Probabilities = choices.ToDictionary(choice => choice,
                choice => choice == selected ? probability : (1 - probability) / (choices.Length - 1)) };
    }

    private static ClaimsPolicyDecision Evaluate(ClaimsWorkflow workflow, ClaimsCase? item = null,
        SystemOneChoiceAnswer? answer = null, ClaimsPolicy? policy = null)
    {
        var result = DecisionExampleRunner.EvaluateClaimsPolicy(workflow, item ?? CompleteCase(),
            policy ?? Policy(workflow), answer ?? Answer(workflow));
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Action));
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Queue));
        Assert.IsTrue(result.Reasons.Count > 0 && result.Reasons.All(reason => !string.IsNullOrWhiteSpace(reason)));
        return result;
    }

    [TestMethod]
    [DataRow(.90, .90, "Continue Workflow")]
    [DataRow(.899999999, .90, "Human Triage")]
    [DataRow(.90, .899999999, "Human Triage")]
    [DataRow(.900000001, .900000001, "Continue Workflow")]
    [DataRow(.94, .12, "Human Triage")]
    public void BothUnroundedCertaintyValuesMustMeetInclusiveThreshold(double probability, double confidence, string expected)
    {
        Assert.AreEqual(expected, Evaluate(ClaimsWorkflow.Validation,
            answer: Answer(ClaimsWorkflow.Validation, probability: probability, confidence: confidence)).Action);
    }

    [TestMethod]
    public void EditableThresholdsChangePolicyWithoutChangingTheModelAnswer()
    {
        var answer = Answer(ClaimsWorkflow.Validation, probability: .92, confidence: .92);
        Assert.AreEqual("Continue Workflow", Evaluate(ClaimsWorkflow.Validation, answer: answer).Action);
        Assert.AreEqual("Human Triage", Evaluate(ClaimsWorkflow.Validation, answer: answer,
            policy: new(.95, .95, 5000m)).Action);
    }

    [TestMethod]
    [DataRow("4999.99", "Continue Workflow")]
    [DataRow("5000", "Continue Workflow")]
    [DataRow("5000.01", "Specialist Review")]
    public void DecimalAuthorityLimitIncludesExactlyFiveThousand(string amount, string expected)
    {
        var item = CompleteCase();
        item = item with { Extracted = item.Extracted with { Amount = decimal.Parse(amount, CultureInfo.InvariantCulture) } };
        Assert.AreEqual(expected, Evaluate(ClaimsWorkflow.Validation, item).Action);
    }

    [TestMethod]
    [DataRow("mandatory")]
    [DataRow("sensitive")]
    [DataRow("high risk")]
    [DataRow("uncertain extraction")]
    public void EachReviewFlagOverridesCertainModelRecommendationsInBothWorkflows(string flag)
    {
        foreach (ClaimsWorkflow workflow in Enum.GetValues<ClaimsWorkflow>())
        {
            var item = CompleteCase();
            var flags = item.Flags;
            flags = flag switch
            {
                "mandatory" => flags with { MandatoryReview = true },
                "sensitive" => flags with { Sensitive = true },
                "high risk" => flags with { HighRisk = true },
                _ => flags with { ExtractionUncertain = true }
            };
            Assert.AreEqual("Specialist Review", Evaluate(workflow, item with { Flags = flags },
                Answer(workflow, probability: 1, confidence: 1)).Action, workflow.ToString());
        }
    }

    [TestMethod]
    public void InconsistencyAndDuplicateEvidenceOverrideModelContinuationAndMissingInformation()
    {
        var item = CompleteCase();
        Assert.AreEqual("Specialist Review", Evaluate(ClaimsWorkflow.Validation,
            item with { Flags = item.Flags with { InternallyConsistent = false } }).Action);
        Assert.AreEqual("Duplicate Review", Evaluate(ClaimsWorkflow.Validation,
            item with { Extracted = item.Extracted with { LossDate = null },
                Flags = item.Flags with { PossibleDuplicate = true } }).Action);
        Assert.AreEqual("Specialist Review", Evaluate(ClaimsWorkflow.Validation,
            item with { Flags = item.Flags with { PossibleDuplicate = true, MandatoryReview = true } }).Action);
    }

    [TestMethod]
    [DataRow("claimant")]
    [DataRow("contact")]
    [DataRow("policy")]
    [DataRow("loss date")]
    [DataRow("loss description")]
    public void MissingIntakeFieldsPreventProvisionalCreationAndValidationContinuation(string field)
    {
        var item = CompleteCase();
        var fields = item.Extracted;
        fields = field switch
        {
            "claimant" => fields with { ClaimantName = null },
            "contact" => fields with { ContactEmail = null },
            "policy" => fields with { PolicyNumber = null },
            "loss date" => fields with { LossDate = null },
            _ => fields with { LossDescription = null }
        };
        foreach (ClaimsWorkflow workflow in Enum.GetValues<ClaimsWorkflow>())
            Assert.AreEqual(field == "contact" ? "Human Triage" : "Request Additional Information",
                Evaluate(workflow, item with { Extracted = fields }).Action, workflow.ToString());
    }

    [TestMethod]
    [DataRow("Document Submission", "Route Documents")]
    [DataRow("Status Inquiry", "Route Status Inquiry")]
    public void ExistingClaimRoutingRequiresAClaimReference(string choice, string expected)
    {
        var item = CompleteCase();
        var answer = Answer(ClaimsWorkflow.EmailIntake, choice);
        Assert.AreEqual(expected, Evaluate(ClaimsWorkflow.EmailIntake, item, answer).Action);
        Assert.AreEqual("Request Additional Information", Evaluate(ClaimsWorkflow.EmailIntake,
            item with { Extracted = item.Extracted with { ClaimReference = null } }, answer).Action);
        Assert.AreEqual("Human Triage", Evaluate(ClaimsWorkflow.EmailIntake,
            item with { Extracted = item.Extracted with { ClaimReference = null, ContactEmail = null } }, answer).Action);
    }

    [TestMethod]
    [DataRow("Complaint", "Route Complaint")]
    [DataRow("Unrelated Correspondence", "Route General Correspondence")]
    public void NonIntakeCorrespondenceUsesItsDedicatedQueue(string choice, string expected) =>
        Assert.AreEqual(expected, Evaluate(ClaimsWorkflow.EmailIntake,
            answer: Answer(ClaimsWorkflow.EmailIntake, choice)).Action);

    [TestMethod]
    [DataRow("Request Additional Information")]
    [DataRow("Duplicate Review")]
    [DataRow("Specialist Review")]
    public void ModelTreatmentNeverGetsPromotedToContinuation(string choice) =>
        Assert.AreEqual(choice, Evaluate(ClaimsWorkflow.Validation,
            answer: Answer(ClaimsWorkflow.Validation, choice)).Action);

    [TestMethod]
    [DataRow("amount")]
    [DataRow("currency")]
    [DataRow("non USD")]
    [DataRow("reference")]
    public void UnknownAmountCurrencyAndClaimReferenceBlockContinuation(string missing)
    {
        var item = CompleteCase();
        var fields = missing switch
        {
            "amount" => item.Extracted with { Amount = null },
            "currency" => item.Extracted with { Currency = null },
            "non USD" => item.Extracted with { Currency = "EUR" },
            _ => item.Extracted with { ClaimReference = null }
        };
        Assert.AreNotEqual("Continue Workflow", Evaluate(ClaimsWorkflow.Validation,
            item with { Extracted = fields }).Action);
    }

    [TestMethod]
    public void MissingInformationCannotBypassCertaintyOrDowngradeAHumanReviewRecommendation()
    {
        var item = CompleteCase();
        item = item with { Extracted = item.Extracted with { LossDate = null } };
        Assert.AreEqual("Human Triage", Evaluate(ClaimsWorkflow.Validation, item,
            Answer(ClaimsWorkflow.Validation, probability: .45)).Action);
        foreach (string choice in new[] { "Duplicate Review", "Specialist Review" })
            Assert.AreEqual(choice, Evaluate(ClaimsWorkflow.Validation, item,
                Answer(ClaimsWorkflow.Validation, choice)).Action);
    }

    [TestMethod]
    public void ProvisionalIntakeDoesNotRequireAnExistingClaimReferenceOrAmount()
    {
        var item = CompleteCase();
        item = item with { Extracted = item.Extracted with { ClaimReference = null, Amount = null, Currency = null } };
        Assert.AreEqual("Create Provisional Claim", Evaluate(ClaimsWorkflow.EmailIntake, item).Action);
    }

    [TestMethod]
    [DataRow("alex")]
    [DataRow("alex@")]
    [DataRow(" ")]
    public void UnusableContactCannotReceiveAnAutomatedInformationRequest(string contact)
    {
        var item = CompleteCase();
        item = item with { Extracted = item.Extracted with { ContactEmail = contact, LossDate = null } };
        foreach (ClaimsWorkflow workflow in Enum.GetValues<ClaimsWorkflow>())
            Assert.AreEqual("Human Triage", Evaluate(workflow, item).Action);
    }

    [TestMethod]
    public void TiedOrNonMaximumSelectedChoiceCannotEnableAutomaticAction()
    {
        var policy = new ClaimsPolicy(.1, .1, 5000m);
        var tied = Answer(ClaimsWorkflow.Validation, probability: .5, confidence: 1);
        tied.Probabilities["Request Additional Information"] = .5;
        tied.Probabilities["Duplicate Review"] = tied.Probabilities["Specialist Review"] = 0;
        Assert.AreEqual("Human Triage", Evaluate(ClaimsWorkflow.Validation, answer: tied, policy: policy).Action);
        var nonMaximum = Answer(ClaimsWorkflow.Validation, probability: .2, confidence: 1);
        nonMaximum.Probabilities["Request Additional Information"] = .6;
        nonMaximum.Probabilities["Duplicate Review"] = nonMaximum.Probabilities["Specialist Review"] = .1;
        Assert.AreEqual("Human Triage", Evaluate(ClaimsWorkflow.Validation, answer: nonMaximum, policy: policy).Action);
    }

    [TestMethod]
    public void MalformedDistributionsCannotReachPolicyDecisionsEvenWithMandatoryReview()
    {
        var item = CompleteCase();
        item = item with { Flags = item.Flags with { MandatoryReview = true } };
        foreach (string kind in new[] { "missing", "extra", "negative", "sum", "confidence", "unknown choice" })
        {
            var answer = Answer(ClaimsWorkflow.Validation);
            switch (kind)
            {
                case "missing": answer.Probabilities.Remove("Specialist Review"); break;
                case "extra": answer.Probabilities["Approve Claim"] = 0; break;
                case "negative": answer.Probabilities["Specialist Review"] = -.1; break;
                case "sum": answer.Probabilities["Continue Workflow"] = .99; break;
                case "confidence": answer = new() { Choice = answer.Choice, Probabilities = answer.Probabilities, Confidence = double.NaN }; break;
                case "unknown choice": answer = new() { Choice = "Approve Claim", Probabilities = answer.Probabilities, Confidence = .94 }; break;
            }
            Assert.ThrowsExactly<InvalidDataException>(() =>
                DecisionExampleRunner.EvaluateClaimsPolicy(ClaimsWorkflow.Validation, item, Policy(ClaimsWorkflow.Validation), answer), kind);
        }
    }
}
