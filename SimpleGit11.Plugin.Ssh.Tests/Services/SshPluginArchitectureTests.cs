using System.Reflection;
using SimpleGit11.Plugin.Ssh.Services;

namespace SimpleGit11.Plugin.Ssh.Tests.Services;

[TestClass]
public sealed class SshPluginArchitectureTests
{
    [TestMethod]
    public void Plugin_DoesNotReferenceApplicationAssembly()
    {
        Assert.IsFalse(typeof(SshPlugin).Assembly.GetReferencedAssemblies().Any(name => name.Name == "SimpleGit11"));
        Assert.AreEqual("SimpleGit11.Extensibility", typeof(SimpleGit11.Services.GitCommandException).Assembly.GetName().Name);
    }

    [TestMethod]
    public void MetadataVersion_ComesFromPluginAssembly()
    {
        Assembly assembly = typeof(SshPlugin).Assembly;
        string informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;

        Assert.AreEqual(informationalVersion.Split('+', 2)[0], new SshPlugin().Metadata.Version);
    }

    [TestMethod]
    public void ServiceLayer_DoesNotDependOnWinUi()
    {
        Type[] types = typeof(SshExecutionProvider).Assembly.GetTypes()
            .Where(type => type.Namespace == "SimpleGit11.Plugin.Ssh.Services").ToArray();
        foreach (Type type in types)
        {
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            Type[] references = type.GetFields(flags).Select(field => field.FieldType)
                .Concat(type.GetMethods(flags).Select(method => method.ReturnType))
                .Concat(type.GetMethods(flags).SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType)))
                .Concat(type.GetConstructors(flags).SelectMany(constructor => constructor.GetParameters().Select(parameter => parameter.ParameterType)))
                .ToArray();
            Assert.IsFalse(references.Any(ReferencesWinUi), $"UI dependency in {type.FullName}");
        }
    }

    private static bool ReferencesWinUi(Type type) => type.Namespace?.StartsWith("Microsoft.UI", StringComparison.Ordinal) == true
        || (type.HasElementType && ReferencesWinUi(type.GetElementType()!))
        || (type.IsGenericType && type.GenericTypeArguments.Any(ReferencesWinUi));
}
