using MatPlatform;

namespace MatSplit.Tests;

/// <summary>The definition of MatSplit is valid for MatPlatform's validator, in both languages and against MatSplit's copy of the MatUi sprite.</summary>
public sealed class PlatformValidatorTests(SplitFactory site) : IClassFixture<SplitFactory>
{
    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    public void The_description_is_valid(string language)
        => Assert.Empty(MatPlatformValidator.Validate(MatPlatformBuilder.Build(Web.Platform.SplitPlatform.Describe, language), Sprite()));

    [Fact]
    public async Task The_served_definition_is_valid()
        => Assert.Empty(MatPlatformValidator.Validate(await site.CreateClient().GetStringAsync("/api"), Sprite()));

    private static IReadOnlySet<string> Sprite()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MatSplit.sln")))
        {
            dir = dir.Parent;
        }

        return MatPlatformValidator.ReadSprite(Path.Combine(dir!.FullName, "src", "MatSplit.Web", "wwwroot", "matui", "matui-icons.svg"));
    }
}
