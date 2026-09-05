using Microsoft.Testing.Platform.Extensions.CommandLine;
using Xunit;

namespace Raun.Mtp.Test;

public class HtmlReportOptionsProviderTests
{
    [Fact]
    public void Registers_the_flag_and_filename_options()
    {
        var provider = new HtmlReport.HtmlReportOptionsProvider();
        var names = provider.GetCommandLineOptions().Select(o => o.Name).ToList();

        Assert.Contains("report-html", names);
        Assert.Contains("report-html-filename", names);
    }

    [Fact]
    public void The_flag_takes_no_argument_and_the_filename_takes_exactly_one()
    {
        var provider = new HtmlReport.HtmlReportOptionsProvider();
        var byName = provider.GetCommandLineOptions().ToDictionary(o => o.Name);

        Assert.Equal(ArgumentArity.Zero, byName["report-html"].Arity);
        Assert.Equal(ArgumentArity.ExactlyOne, byName["report-html-filename"].Arity);
    }

    [Fact]
    public async Task Filename_argument_must_be_non_empty()
    {
        var provider = new HtmlReport.HtmlReportOptionsProvider();
        var filename = provider.GetCommandLineOptions().Single(o => o.Name == "report-html-filename");

        var ok = await provider.ValidateOptionArgumentsAsync(filename, ["report.html"]);
        var bad = await provider.ValidateOptionArgumentsAsync(filename, [""]);

        Assert.True(ok.IsValid);
        Assert.False(bad.IsValid);
    }
}
