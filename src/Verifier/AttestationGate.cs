using ClaimSeeker.Core;

namespace ClaimSeeker.Verifier;

/// <summary>
/// Decides whether a letter may leave the engine as a draft. It is a pure function of the request and
/// the context it is given, and it has no setting that turns it off.
///
/// A letter passes only when all of this is true:
///
/// - Every sentence about a person is filled from a fact the account holder attested, of the right
///   kind, about the right person, word for word.
/// - Every detail about the property is exactly what the source reported, and is inert text: no
///   link, no address, no markup. What a website returned is never trusted to steer a letter.
/// - Every link is an official address from the source registry. The letter's recipient is either
///   left for the person to fill in or is one the registry supplied.
/// - The letter is of a kind the match supports. Wording that states ownership needs a match the
///   engine trusts; a "possible" match can only ask.
/// - The letter says nothing that has not happened. A follow-up on a filed claim needs a claim the
///   person has reported filing.
/// - No government or financial identifier appears anywhere in it.
///
/// Anything else is blocked, and the reasons are returned so the person can see what was missing.
/// </summary>
public static class AttestationGate
{
    public const int MaxSubjectLength = 200;
    public const int MaxBodyLength = 8000;

    public static GateResult Evaluate(DraftRequest request, GateContext context)
    {
        var violations = new List<Violation>();
        var template = request.Template;

        foreach (var problem in TemplateProblems(template))
            violations.Add(new Violation("template-invalid", problem));

        if (request.Confidence < template.MinConfidence)
            violations.Add(new Violation("confidence-too-low",
                $"This letter needs a match of at least \"{template.MinConfidence}\"; this one is \"{request.Confidence}\"."));

        if (template.AllowedCaseStates is { } allowed && !allowed.Contains(request.CaseState))
            violations.Add(new Violation("case-state",
                $"This letter can be written only when the case is {string.Join(" or ", allowed.Select(CaseStates.Words))}; it is {CaseStates.Words(request.CaseState)}."));

        var bound = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var binding in request.Bindings)
        {
            var spec = template.Slot(binding.Slot);
            if (spec is null)
            {
                violations.Add(new Violation("unknown-slot", $"The letter has no slot named \"{binding.Slot}\"."));
                continue;
            }

            if (!bound.TryAdd(binding.Slot, binding.Value))
            {
                violations.Add(new Violation("duplicate-slot", $"The slot \"{binding.Slot}\" was filled twice."));
                continue;
            }

            CheckProvenance(request, context, spec, binding, violations);
        }

        var (subjectLine, body, missing, used) = Render(template, bound);
        foreach (var slot in missing)
            violations.Add(new Violation("unfilled-slot", $"Nothing attested or reported fills \"{slot}\", which this letter needs."));

        // A filled slot that no sentence used means the letter came out differently from how it was
        // composed, for example a city given without a state. That is a broken letter, not a shorter one.
        foreach (var slot in bound.Keys)
            if (!used.Contains(slot))
                violations.Add(new Violation("unused-slot", $"\"{slot}\" was filled, but no sentence in the finished letter uses it."));

        if (missing.Count == 0 && (subjectLine.Contains('{') || body.Contains('{')))
            violations.Add(new Violation("stray-brace", "The finished letter contains a brace, which only an unfilled slot should."));
        if (subjectLine.Length > MaxSubjectLength || subjectLine.Any(char.IsControl))
            violations.Add(new Violation("bad-subject", "The subject line is too long or is not a single line."));
        if (body.Length > MaxBodyLength)
            violations.Add(new Violation("too-long", "The letter is too long."));

        if (SensitiveIdentifiers.Find(subjectLine + "\n" + body) is { } kind)
            violations.Add(new Violation("sensitive-identifier", $"The letter contains what looks like a {kind}. Identity is proved through the holder's secure process, never in a letter."));

        CheckLinks(body, template, bound, violations);

        if (request.Recipient is { } recipient
            && !(context.RegistryValues.TryGetValue("recipient", out var official) && official == recipient))
            violations.Add(new Violation("recipient-not-official", "The address the letter is for did not come from the source registry."));

        if (violations.Count > 0) return new GateResult(null, violations);
        return new GateResult(
            new GatePassedDraft(template.Id, request.FindingId, request.SenderSubjectId, subjectLine, body, request.Recipient),
            []);
    }

    private static void CheckProvenance(DraftRequest request, GateContext context, SlotSpec spec, SlotBinding binding, List<Violation> violations)
    {
        switch (spec.Source)
        {
            case SlotSource.Fact:
                var fact = context.Facts.FirstOrDefault(f => f.Id == binding.Reference);
                var person = spec.Person == SlotPerson.Sender ? request.SenderSubjectId : request.SubjectId;
                if (fact is null)
                    violations.Add(new Violation("fact-not-attested", $"\"{binding.Slot}\" is not backed by an attested fact."));
                else if (fact.SubjectId != person)
                    violations.Add(new Violation("fact-wrong-person", $"\"{binding.Slot}\" is filled from a fact about someone else."));
                else if (spec.FactKind is { } kind && fact.Kind != kind)
                    violations.Add(new Violation("fact-wrong-kind", $"\"{binding.Slot}\" needs a {kind} fact, not a {fact.Kind} one."));
                else if (fact.Value != binding.Value)
                    violations.Add(new Violation("fact-value-mismatch", $"\"{binding.Slot}\" does not say what was attested."));
                break;

            case SlotSource.FindingField:
                if (spec.Field is null || binding.Reference != spec.Field)
                    violations.Add(new Violation("wrong-field", $"\"{binding.Slot}\" may only carry the finding's {spec.Field ?? "(unset)"}."));
                else if (!context.FindingFields.TryGetValue(binding.Reference, out var reported) || string.IsNullOrEmpty(reported))
                    violations.Add(new Violation("finding-field-empty", $"The source reported nothing for \"{binding.Slot}\"."));
                else if (reported != binding.Value)
                    violations.Add(new Violation("finding-value-mismatch", $"\"{binding.Slot}\" does not say what the source reported."));
                else if (!UntrustedText.IsInert(reported))
                    violations.Add(new Violation("untrusted-content", $"What the source reported for \"{binding.Slot}\" contains a link, an address or markup, so it cannot go in a letter."));
                break;

            case SlotSource.Registry:
                if (spec.Field is null || binding.Reference != spec.Field)
                    violations.Add(new Violation("wrong-field", $"\"{binding.Slot}\" may only carry the registry's {spec.Field ?? "(unset)"}."));
                else if (!context.RegistryValues.TryGetValue(binding.Reference, out var official) || official != binding.Value)
                    violations.Add(new Violation("registry-value-mismatch", $"\"{binding.Slot}\" is not the value in the source registry."));
                break;
        }
    }

    /// <summary>Every link in the letter must be, in full, a value a registry slot supplied.</summary>
    private static void CheckLinks(string body, DraftTemplate template, Dictionary<string, string> bound, List<Violation> violations)
    {
        var official = template.Slots
            .Where(s => s.Source == SlotSource.Registry && bound.ContainsKey(s.Name))
            .Select(s => bound[s.Name])
            .ToHashSet(StringComparer.Ordinal);

        var index = 0;
        while ((index = body.IndexOf("http", index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var end = index;
            while (end < body.Length && !char.IsWhiteSpace(body[end])) end++;
            var link = body[index..end].TrimEnd('.', ',', ')', ';');
            if (!official.Contains(link))
            {
                violations.Add(new Violation("link-not-official", "The letter contains a link that is not an official address from the source registry."));
                return;
            }

            index = end;
        }
    }

    /// <summary>
    /// The letter's text with its slots filled, and the slots an included paragraph needed but did not
    /// get. A paragraph is included when all of its When slots are filled and none of its Unless slots are.
    /// </summary>
    public static (string SubjectLine, string Body, IReadOnlyList<string> Missing, IReadOnlySet<string> Used) Render(
        DraftTemplate template, IReadOnlyDictionary<string, string> bound)
    {
        var missing = new List<string>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        string Fill(string text)
        {
            // Slots are filled in one pass over the fixed wording, so text that arrives in one slot
            // can never be read as a placeholder for another.
            var result = new System.Text.StringBuilder(text.Length + 64);
            var i = 0;
            while (i < text.Length)
            {
                var open = text.IndexOf('{', i);
                var close = open < 0 ? -1 : text.IndexOf('}', open + 1);
                if (open < 0 || close < 0) break;

                result.Append(text, i, open - i);
                var slot = text[(open + 1)..close];
                if (bound.TryGetValue(slot, out var value))
                {
                    result.Append(value);
                    used.Add(slot);
                }
                else
                {
                    result.Append('{').Append(slot).Append('}');
                    if (!missing.Contains(slot)) missing.Add(slot);
                }

                i = close + 1;
            }

            result.Append(text, i, text.Length - i);
            return result.ToString();
        }

        var paragraphs = new List<string>();
        foreach (var paragraph in template.Paragraphs)
        {
            if (paragraph.When is { } required && !required.All(bound.ContainsKey)) continue;
            if (paragraph.Unless is { } excluded && excluded.Any(bound.ContainsKey)) continue;
            paragraphs.Add(Fill(paragraph.Text));
        }

        return (Fill(template.SubjectLine), string.Join("\n\n", paragraphs), missing, used);
    }

    /// <summary>
    /// What is wrong with a template itself, whatever it is filled with. Built-in templates are held to
    /// this by a harness; the gate applies it again so a template from anywhere else gets no easier ride.
    /// </summary>
    public static IReadOnlyList<string> TemplateProblems(DraftTemplate template)
    {
        var problems = new List<string>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slot in template.Slots)
        {
            if (!names.Add(slot.Name)) problems.Add($"The slot \"{slot.Name}\" is declared twice.");
            if (slot.Source == SlotSource.Fact && slot.FactKind is null) problems.Add($"The fact slot \"{slot.Name}\" does not say what kind of fact it takes.");
            if (slot.Source != SlotSource.Fact && string.IsNullOrEmpty(slot.Field)) problems.Add($"The slot \"{slot.Name}\" does not name its field.");
        }

        var fixedText = template.SubjectLine + "\n" + string.Join("\n", template.Paragraphs.Select(p => p.Text));
        foreach (var placeholder in Placeholders(fixedText))
            if (!names.Contains(placeholder)) problems.Add($"The text refers to \"{placeholder}\", which is not a declared slot.");

        foreach (var paragraph in template.Paragraphs)
            foreach (var condition in (paragraph.When ?? []).Concat(paragraph.Unless ?? []))
                if (!names.Contains(condition)) problems.Add($"A paragraph depends on \"{condition}\", which is not a declared slot.");

        // The fixed wording carries no links and no numbers; both can only arrive through a checked slot.
        if (fixedText.Contains("http", StringComparison.OrdinalIgnoreCase) || fixedText.Contains("www.", StringComparison.OrdinalIgnoreCase))
            problems.Add("The fixed wording contains a link.");
        if (!SensitiveIdentifiers.IsClean(fixedText)) problems.Add("The fixed wording contains a long number.");

        if (template.Class == TemplateClass.Assertion && template.MinConfidence < Confidence.Likely)
            problems.Add("A letter that states ownership must require at least a likely match.");

        return problems;
    }

    private static IReadOnlyList<string> Placeholders(string text)
    {
        var names = new List<string>();
        var i = 0;
        while (i < text.Length)
        {
            var open = text.IndexOf('{', i);
            if (open < 0) break;
            var close = text.IndexOf('}', open + 1);
            if (close < 0) break;
            var name = text[(open + 1)..close];
            if (!names.Contains(name)) names.Add(name);
            i = close + 1;
        }

        return names;
    }
}
