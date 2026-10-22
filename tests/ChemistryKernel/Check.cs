namespace RustyChemistry.Checks;

internal static class Check
{
    private static int passed;
    private static int failed;

    internal static void That(bool condition, string expectation)
    {
        if (condition)
        {
            passed++;
            return;
        }

        failed++;
        Console.Error.WriteLine($"FAIL: {expectation}");
    }

    internal static void Near(double actual, double expected, double tolerance, string expectation) =>
        That(Math.Abs(actual - expected) <= tolerance, $"{expectation} (expected {expected} ± {tolerance}, got {actual})");

    internal static int Finish()
    {
        Console.WriteLine($"{passed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }
}
