using System.Reflection;
using CommunityToolkit.Mvvm.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SimpleGit11.Services;
using SimpleGit11.Services.Git;
using SimpleGit11.ViewModels;

namespace SimpleGit11.Tests.Architecture;

[TestClass]
public sealed class ServiceLayerArchitectureTests
{
    private const string ServiceNamespacePrefix = "SimpleGit11.Services";
    private const string MicrosoftUiNamespacePrefix = "Microsoft.UI";

    [TestMethod]
    public void ServiceLayer_DoesNotReferenceMicrosoftUiTypes()
    {
        Assembly applicationAssembly = typeof(ISettingsService).Assembly;
        List<string> violations = applicationAssembly
            .GetTypes()
            .Where(IsServiceType)
            .SelectMany(type => GetReferencedTypes(type)
                .Where(ReferencesMicrosoftUi)
                .Select(reference => $"{type.FullName} -> {reference.FullName}"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.AreEqual(
            0,
            violations.Count,
            $"Service-layer types must not reference Microsoft.UI:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [TestMethod]
    public void ViewModels_UseGitServiceAsTheOnlyGitEntryPoint()
    {
        Assembly applicationAssembly = typeof(ViewModelBase).Assembly;
        List<string> violations = applicationAssembly
            .GetTypes()
            .Where(type => type.Namespace == "SimpleGit11.ViewModels"
                || type.Namespace?.StartsWith("SimpleGit11.ViewModels.", StringComparison.Ordinal) == true)
            .SelectMany(type => GetReferencedTypes(type)
                .Where(IsForbiddenGitDependency)
                .Select(reference => $"{type.FullName} -> {reference.FullName}"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.AreEqual(
            0,
            violations.Count,
            $"ViewModels must access Git through IGitService only:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [TestMethod]
    public void RelayCommandHandlers_UseOnPrefix()
    {
        Assembly applicationAssembly = typeof(ViewModelBase).Assembly;
        List<string> violations = applicationAssembly
            .GetTypes()
            .Where(type => type.Namespace == "SimpleGit11.ViewModels"
                || type.Namespace?.StartsWith("SimpleGit11.ViewModels.", StringComparison.Ordinal) == true)
            .SelectMany(type => type
                .GetMethods(BindingFlags.Instance
                    | BindingFlags.Static
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly)
                .Where(method => method.GetCustomAttribute<RelayCommandAttribute>() is not null)
                .Where(method => !method.Name.StartsWith("On", StringComparison.Ordinal))
                .Select(method => $"{type.FullName}.{method.Name}"))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.AreEqual(
            0,
            violations.Count,
            $"RelayCommand handlers must use the On<Action>[Async] naming convention:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    private static bool IsServiceType(Type type)
    {
        return type.Namespace == ServiceNamespacePrefix
            || type.Namespace?.StartsWith($"{ServiceNamespacePrefix}.", StringComparison.Ordinal) == true;
    }

    private static IEnumerable<Type> GetReferencedTypes(Type type)
    {
        if (type.BaseType is not null)
        {
            yield return type.BaseType;
        }

        foreach (Type interfaceType in type.GetInterfaces())
        {
            yield return interfaceType;
        }

        const BindingFlags bindingFlags = BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.DeclaredOnly;

        foreach (FieldInfo field in type.GetFields(bindingFlags))
        {
            yield return field.FieldType;
        }

        foreach (PropertyInfo property in type.GetProperties(bindingFlags))
        {
            yield return property.PropertyType;
            foreach (ParameterInfo parameter in property.GetIndexParameters())
            {
                yield return parameter.ParameterType;
            }
        }

        foreach (ConstructorInfo constructor in type.GetConstructors(bindingFlags))
        {
            foreach (ParameterInfo parameter in constructor.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }

        foreach (MethodInfo method in type.GetMethods(bindingFlags))
        {
            yield return method.ReturnType;
            foreach (ParameterInfo parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }
    }

    private static bool ReferencesMicrosoftUi(Type type)
    {
        if (type.Namespace == MicrosoftUiNamespacePrefix
            || type.Namespace?.StartsWith($"{MicrosoftUiNamespacePrefix}.", StringComparison.Ordinal) == true)
        {
            return true;
        }

        if (type.HasElementType && type.GetElementType() is Type elementType)
        {
            return ReferencesMicrosoftUi(elementType);
        }

        return type.IsGenericType
            && type.GetGenericArguments().Any(ReferencesMicrosoftUi);
    }

    private static bool IsForbiddenGitDependency(Type type)
    {
        return type != typeof(IGitService)
            && (type == typeof(IGitOperationQueue)
                || type.IsInterface
                    && type.Namespace == "SimpleGit11.Services"
                    && type.Name.StartsWith("IGit", StringComparison.Ordinal));
    }
}
