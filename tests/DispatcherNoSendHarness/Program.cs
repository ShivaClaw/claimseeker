// DispatcherNoSendHarness: the dispatcher makes drafts and can do nothing else. That is checked on
// the compiled assembly itself, not on its documentation: its public surface, every member name in
// it, and every assembly it references.
using System.Reflection;
using System.Text;
using ClaimSeeker.Core;
using ClaimSeeker.Dispatcher;
using ClaimSeeker.Preparer;
using ClaimSeeker.Store;
using ClaimSeeker.Verifier;
using static Harness;

Title("ClaimSeeker dispatcher: drafts only");
var assembly = typeof(IDraftOutbox).Assembly;
const BindingFlags Everything = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

// ---------- the assembly ----------
Section("There is no send path to find");
var forbidden = new[] { "Send", "Submit", "Transmit", "Smtp", "Deliver", "Upload", "Post" };
var memberNames = assembly.GetTypes()
    .Where(t => !t.Name.StartsWith('<'))
    .SelectMany(t => t.GetMembers(Everything).Select(m => $"{t.Name}.{m.Name}").Prepend(t.Name))
    .ToList();
foreach (var word in forbidden)
    Check($"no type or member is named after \"{word}\"", !memberNames.Any(n => n.Contains(word, StringComparison.OrdinalIgnoreCase)),
        string.Join(", ", memberNames.Where(n => n.Contains(word, StringComparison.OrdinalIgnoreCase))));

var references = assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();
foreach (var network in new[] { "System.Net.Http", "System.Net.Mail", "System.Net.Sockets", "System.Net.Requests", "System.Net.WebSockets", "System.Net.Security", "System.Net.NameResolution" })
    Check($"the assembly does not reference {network}", !references.Contains(network), string.Join(", ", references));
Check("it references no network assembly at all", !references.Any(r => r.StartsWith("System.Net", StringComparison.Ordinal)), string.Join(", ", references));
Check("it does not reference a browser or a database", !references.Any(r => r.Contains("Playwright", StringComparison.Ordinal) || r.Contains("Sqlite", StringComparison.Ordinal)));

var outbox = typeof(IDraftOutbox);
var methods = outbox.GetMethods();
Equal("the outbox has exactly one method", methods.Length, 1);
Equal("and it makes a draft", methods[0].Name, "CreateDraft");
Check("it accepts only a letter the gate passed", methods[0].GetParameters() is [{ ParameterType: var only }] && only == typeof(GatePassedDraft));
Check("the outbox has no properties or events", outbox.GetProperties().Length == 0 && outbox.GetEvents().Length == 0);
Equal("the assembly's public types are the outbox, its file form, the receipt and the writer",
    string.Join(",", assembly.GetExportedTypes().Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal)), "DraftReceipt,EmlWriter,FileDraftOutbox,IDraftOutbox");

// ---------- writing a draft ----------
Section("A passed letter becomes an unsent message file");
var household = SampleHousehold.Load();
GatePassedDraft Passed(string owner = "SAMPLE JANE", string? holder = "PRAIRIE STATE ELECTRIC COOP")
{
    var finding = new FindingRow
    {
        Id = "f_test", SubjectId = "self", SourceId = "missingmoney", DedupeKey = "d", OwnerName = owner, HolderName = holder,
        City = "SPRINGFIELD", UsState = "IL", Family = nameof(PropertyFamily.Utility), Confidence = "Strong", NameMatch = "ExactCurrent",
        Rationale = "", QueryKey = "q", CaseState = "PACKET_READY", FirstSeen = "t", LastSeen = "t", UpdatedAt = "t",
    };
    var request = DraftComposer.Compose(LetterTemplates.Find(LetterTemplates.HolderInquiry)!, finding, household);
    return AttestationGate.Evaluate(request, DraftComposer.ContextFor(finding, household)).Draft
           ?? throw new InvalidOperationException("The gate blocked a letter this harness expects to pass.");
}

using var temp = NewTempFolder("drafts");
var files = new FileDraftOutbox(temp.File("drafts"));
var draft = Passed();
var receipt = files.CreateDraft(draft);
var bytes = File.ReadAllBytes(receipt.Path);
var text = Encoding.UTF8.GetString(bytes);

Check("the file is written", File.Exists(receipt.Path) && !receipt.AlreadyExisted);
Check("it is an .eml named for the finding and the letter", Path.GetFileName(receipt.Path).StartsWith("f_test-holder-inquiry-", StringComparison.Ordinal) && receipt.Path.EndsWith(".eml", StringComparison.Ordinal));
Check("it is marked unsent, so a mail program opens it as a draft", text.StartsWith("X-Unsent: 1\r\n", StringComparison.Ordinal));
Check("the subject is the gate's subject", text.Contains("\r\nSubject: Inquiry about unclaimed property reported for SAMPLE JANE\r\n", StringComparison.Ordinal));
Check("it is plain text in UTF-8", text.Contains("Content-Type: text/plain; charset=utf-8\r\n", StringComparison.Ordinal));
Check("every line ends the way mail requires", !text.Replace("\r\n", "", StringComparison.Ordinal).Contains('\n') && !text.Replace("\r\n", "", StringComparison.Ordinal).Contains('\r'));
Check("the body is the gate's body, unchanged", text.Split("\r\n\r\n", 2)[1].Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n') == draft.Body);
Check("no recipient is invented", !text.Contains("\r\nTo:", StringComparison.Ordinal));
Check("no sender, copy or reply address is set", !text.Contains("From:", StringComparison.Ordinal) && !text.Contains("Cc:", StringComparison.OrdinalIgnoreCase) && !text.Contains("Reply-To:", StringComparison.OrdinalIgnoreCase));
Check("no internal id rides along in a header", !text.Split("\r\n\r\n", 2)[0].Contains("f_test", StringComparison.Ordinal));
Check("the receipt's hash is the file's", receipt.Sha256 == Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant());
Check("no half-written file is left beside it", Directory.GetFiles(temp.File("drafts")).Length == 1);

Section("The same letter twice is one file");
var again = files.CreateDraft(Passed());
Check("the second write reports the file was already there", again.AlreadyExisted && again.Path == receipt.Path && again.Sha256 == receipt.Sha256);
Check("and there is still one file", Directory.GetFiles(temp.File("drafts")).Length == 1);
var different = files.CreateDraft(Passed(holder: "MIDWEST CABLE"));
Check("a different letter is a different file", different.Path != receipt.Path && Directory.GetFiles(temp.File("drafts")).Length == 2);

Section("Headers cannot be forged through the text");
Check("a subject with a line break is refused", Throws<ArgumentException>(() => EmlWriter.Write("Hello\r\nBcc: someone@example.com", "body", null)));
Check("a subject with a tab or other control character is refused", Throws<ArgumentException>(() => EmlWriter.Write("Hello\u0007", "body", null)));
Check("a recipient that is not a plain address is refused", Throws<ArgumentException>(() => EmlWriter.Write("s", "b", "Name <a@b.example>, c@d.example")));
Check("a recipient with a line break is refused", Throws<ArgumentException>(() => EmlWriter.Write("s", "b", "a@b.example\r\nBcc: c@d.example")));
Check("a plain recipient is written", Encoding.UTF8.GetString(EmlWriter.Write("s", "b", "unclaimed@treasurer.example")).Contains("\r\nTo: unclaimed@treasurer.example\r\n", StringComparison.Ordinal));
{
    var encoded = EmlWriter.EncodeHeader("Inquiry about unclaimed property reported for PEÑA JOSÉ");
    Check("a subject with accents is sent as encoded words", encoded.StartsWith("=?UTF-8?B?", StringComparison.Ordinal) && encoded.All(c => c is >= ' ' and <= '~' or '\r' or '\n'));
    var decoded = string.Concat(encoded.Split("\r\n ").Select(w => Encoding.UTF8.GetString(Convert.FromBase64String(w["=?UTF-8?B?".Length..^2]))));
    Equal("and decodes to exactly what was written", decoded, "Inquiry about unclaimed property reported for PEÑA JOSÉ");
    Check("no encoded word is longer than a header line allows", encoded.Split("\r\n ").All(w => w.Length <= 75));
    Equal("a plain subject is left as it is", EmlWriter.EncodeHeader("Inquiry about SAMPLE JANE"), "Inquiry about SAMPLE JANE");
}
{
    var accented = Passed(owner: "PEÑA JOSÉ");
    var written = Encoding.UTF8.GetString(File.ReadAllBytes(files.CreateDraft(accented).Path));
    Check("a letter with accents in it is written whole", written.Contains("reported under the name \"PEÑA JOSÉ\"", StringComparison.Ordinal));
    Check("its subject header is pure ASCII", written.Split("\r\n\r\n", 2)[0].All(c => c < 128));
}
{
    var longLine = new string('x', 1200);
    var written = Encoding.UTF8.GetString(EmlWriter.Write("s", longLine, null));
    Check("a body line too long for mail is sent encoded", written.Contains("Content-Transfer-Encoding: base64", StringComparison.Ordinal));
    Check("and no line in the file is over the limit", written.Split("\r\n").All(l => l.Length <= 998));
    var body = string.Concat(written.Split("\r\n\r\n", 2)[1].Split("\r\n"));
    Equal("and decodes to exactly what was written", Encoding.UTF8.GetString(Convert.FromBase64String(body)), longLine);
}
Check("a bare line feed in the body becomes a proper line ending",
    Encoding.UTF8.GetString(EmlWriter.Write("s", "one\ntwo\rthree", null)).EndsWith("one\r\ntwo\r\nthree\r\n", StringComparison.Ordinal));

return Finish();
