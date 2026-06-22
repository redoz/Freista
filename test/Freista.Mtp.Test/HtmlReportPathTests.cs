using Xunit;

namespace Freista.Mtp.Test;

public class HtmlReportPathTests
{
    [Fact]
    public void Returns_null_when_the_flag_is_absent()
        => Assert.Null(HtmlReport.HtmlReportPath.Resolve(enabled: false, filename: null, resultsDirectory: @"C:\r"));

    [Fact]
    public void Defaults_the_filename_under_the_results_directory()
    {
        var path = HtmlReport.HtmlReportPath.Resolve(enabled: true, filename: null, resultsDirectory: @"C:\r");
        Assert.Equal(Path.Combine(@"C:\r", "freista-report.html"), path);
    }

    [Fact]
    public void Honors_an_explicit_filename()
    {
        var path = HtmlReport.HtmlReportPath.Resolve(enabled: true, filename: "run.html", resultsDirectory: @"C:\r");
        Assert.Equal(Path.Combine(@"C:\r", "run.html"), path);
    }

    [Fact]
    public void Falls_back_to_current_directory_when_results_directory_is_unknown()
    {
        var path = HtmlReport.HtmlReportPath.Resolve(enabled: true, filename: null, resultsDirectory: null);
        Assert.Equal(Path.Combine(Directory.GetCurrentDirectory(), "freista-report.html"), path);
    }
}
