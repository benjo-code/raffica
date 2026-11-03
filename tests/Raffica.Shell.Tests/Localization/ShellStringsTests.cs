using System.Globalization;
using Raffica.Shell.Localization;
using Xunit;

namespace Raffica.Shell.Tests.Localization;

public sealed class ShellStringsTests
{
    /// <summary>Every text property of <see cref="ShellStrings"/> has an entry in both resource files (a missing key throws).</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void EveryPropertyHasAResourceEntry(string lang)
    {
        var previous = ShellStrings.Culture;
        try
        {
            ShellStrings.Culture = CultureInfo.GetCultureInfo(lang);
            var properties = typeof(ShellStrings).GetProperties()
                .Where(p => p.PropertyType == typeof(string) && p.GetMethod is { IsStatic: true })
                .ToList();
            Assert.NotEmpty(properties);
            foreach (var property in properties)
            {
                var value = (string?)property.GetValue(null);
                Assert.False(string.IsNullOrWhiteSpace(value), $"{property.Name} is empty in {lang}");
            }
        }
        finally
        {
            ShellStrings.Culture = previous;
        }
    }
}
