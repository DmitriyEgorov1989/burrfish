using System.Xml.Linq;
using Burrfish.Application;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Burrfish.Application.Tests.Architecture;

public sealed class DependencyRulesTests
{
    [Fact]
    public void Domain_has_no_outward_project_or_framework_dependencies()
    {
        var project = LoadProject("src", "Burrfish.Domain", "Burrfish.Domain.csproj");

        Assert.Empty(References(project, "ProjectReference"));
        Assert.Empty(References(project, "PackageReference"));
    }

    [Fact]
    public void Application_depends_on_domain_and_mediatr_but_not_outer_projects()
    {
        var project = LoadProject("src", "Burrfish.Application", "Burrfish.Application.csproj");
        var projectReferences = References(project, "ProjectReference");
        var packageReferences = References(project, "PackageReference");

        Assert.Contains(@"..\Burrfish.Domain\Burrfish.Domain.csproj", projectReferences);
        Assert.Contains("MediatR", packageReferences);
        Assert.DoesNotContain(projectReferences, reference => reference?.Contains("Infrastructure") is true);
        Assert.DoesNotContain(projectReferences, reference => reference?.Contains("Api") is true);
    }

    [Fact]
    public void Infrastructure_depends_only_on_application_and_domain()
    {
        var project = LoadProject("src", "Burrfish.Infrastructure", "Burrfish.Infrastructure.csproj");
        var projectReferences = References(project, "ProjectReference");

        Assert.Equal(
            [
                @"..\Burrfish.Application\Burrfish.Application.csproj",
                @"..\Burrfish.Domain\Burrfish.Domain.csproj"
            ],
            projectReferences);
    }

    [Fact]
    public void Api_depends_only_on_application_and_infrastructure()
    {
        var project = LoadProject("src", "Burrfish.Api", "Burrfish.Api.csproj");
        var projectReferences = References(project, "ProjectReference");

        Assert.Equal(
            [
                @"..\Burrfish.Application\Burrfish.Application.csproj",
                @"..\Burrfish.Infrastructure\Burrfish.Infrastructure.csproj"
            ],
            projectReferences);
    }

    [Fact]
    public void Application_registration_provides_mediatr_sender()
    {
        var services = new ServiceCollection();

        services.AddApplication();

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<ISender>());
    }

    private static XDocument LoadProject(params string[] segments)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return XDocument.Load(Path.Combine([root, .. segments]));
    }

    private static string[] References(XDocument project, string elementName) => project
        .Descendants(elementName)
        .Select(reference => reference.Attribute("Include")?.Value
            ?? throw new InvalidOperationException($"{elementName} must declare Include."))
        .ToArray();
}
