using System.Reflection;
using NetArchTest.Rules;
using NUnit.Framework;
using Shouldly;

namespace CompanyAccessManagement.ArchitectureTests;

/// <summary>
/// Dependencies point inwards: Web -> Infrastructure -> Application -> Domain.
/// </summary>
[TestFixture]
public class LayerDependencyTests
{
    private const string ApplicationNamespace = "CompanyAccessManagement.Application";
    private const string InfrastructureNamespace = "CompanyAccessManagement.Infrastructure";
    private const string WebNamespace = "CompanyAccessManagement.Web";

    private static readonly Assembly DomainAssembly = typeof(Domain.Common.DomainRuleViolationException).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(Application.Common.Interfaces.IApplicationDbContext).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(Infrastructure.Data.ApplicationDbContext).Assembly;

    [Test]
    public void Domain_DoesNotDependOnOuterLayersOrFrameworks()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                ApplicationNamespace,
                InfrastructureNamespace,
                WebNamespace,
                "Microsoft.AspNetCore",
                "Microsoft.EntityFrameworkCore")
            .GetResult();

        AssertSuccessful(result);
    }

    [Test]
    public void Application_DoesNotDependOnInfrastructureOrWeb()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(InfrastructureNamespace, WebNamespace)
            .GetResult();

        AssertSuccessful(result);
    }

    [Test]
    public void Infrastructure_DoesNotDependOnWeb()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .ShouldNot()
            .HaveDependencyOn(WebNamespace)
            .GetResult();

        AssertSuccessful(result);
    }

    private static void AssertSuccessful(TestResult result)
        => result.IsSuccessful.ShouldBeTrue(
            "Violating types: " + string.Join(", ", result.FailingTypeNames ?? []));
}
