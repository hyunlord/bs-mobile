using System.Globalization;
using System.Xml;
using System.Xml.Linq;

if (args.Length != 1) { Console.Error.WriteLine("Expected NUnit XML file path."); return 1; }
try
{
    var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
    using var reader = XmlReader.Create(args[0], settings);
    var document = XDocument.Load(reader);
    var root = document.Root ?? throw new InvalidDataException("Missing NUnit test result root.");
    if (root.Name != "test-run")
    {
        throw new InvalidDataException("Expected NUnit test-run root.");
    }

    int Count(string name) => int.Parse(root.Attribute(name)?.Value ?? throw new InvalidDataException("Missing " + name), CultureInfo.InvariantCulture);
    var total = Count("total");
    var passed = Count("passed");
    if (total <= 0 || passed <= 0 || Count("failed") != 0 || root.Attribute("result")?.Value != "Passed")
    {
        throw new InvalidDataException("Failed, empty or non-passing NUnit test run.");
    }

    Console.WriteLine($"UNITY_TEST_PASS {args[0]} total={total} passed={passed}");
    return 0;
}
catch (Exception exception) when (exception is IOException or XmlException or FormatException or OverflowException)
{
    Console.Error.WriteLine("UNITY_TEST_FAIL " + exception.Message);
    return 1;
}
