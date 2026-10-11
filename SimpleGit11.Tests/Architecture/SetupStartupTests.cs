using System.Reflection;

namespace SimpleGit11.Tests.Architecture;

[TestClass]
public sealed class SetupStartupTests
{
    [TestMethod]
    public void BootstrapperEntryPoint_UsesMtaForBurnComInitialization()
    {
        MethodInfo? entryPoint = LoadSetupAssembly().EntryPoint;

        Assert.IsNotNull(entryPoint);
        Assert.IsNotNull(entryPoint.GetCustomAttribute<MTAThreadAttribute>(),
            "Burn initializes COM in MTA; an STA entry point fails with RPC_E_CHANGED_MODE.");
        Assert.IsNull(entryPoint.GetCustomAttribute<STAThreadAttribute>());
    }

    [TestMethod]
    [DataRow(false, "Install", "Welcome")]
    [DataRow(true, "Install", "Maintenance")]
    [DataRow(true, "Uninstall", "Purge")]
    [DataRow(true, "Repair", "Ready")]
    [DataRow(true, "Modify", "Features")]
    public void Detection_SelectsWelcomeOrRequestedMaintenance(bool currentPackageInstalled, string action, string expected)
    {
        Assembly assembly = LoadSetupAssembly();
        Type window = assembly.GetType("SimpleGit11.Setup.MainWindow", throwOnError: true)!;
        MethodInfo method = window.GetMethod("GetInitialStep", BindingFlags.NonPublic | BindingFlags.Static)!;
        object requestedAction = Enum.Parse(method.GetParameters()[1].ParameterType, action);

        Assert.AreEqual(expected, method.Invoke(null, [currentPackageInstalled, requestedAction])!.ToString());
    }

    [TestMethod]
    [DataRow(true, "Ready")]
    [DataRow(false, "Scope")]
    public void Welcome_UpdatesProceedToConfirmationAndNewInstallsSelectScope(bool existingInstallation, string expected)
    {
        Type window = LoadSetupAssembly().GetType("SimpleGit11.Setup.MainWindow", throwOnError: true)!;
        MethodInfo method = window.GetMethod("GetWelcomeNextStep", BindingFlags.NonPublic | BindingFlags.Static)!;

        Assert.AreEqual(expected, method.Invoke(null, [existingInstallation])!.ToString());
    }

    [TestMethod]
    [DataRow("Uninstall", true, "Upgrade", "DependentAddon", "Absent")]
    [DataRow("Install", true, "Upgrade", "Upgrade", "None")]
    [DataRow("Repair", true, "Upgrade", "Upgrade", "None")]
    [DataRow("Uninstall", false, "Upgrade", "Upgrade", "None")]
    [DataRow("Uninstall", true, "Downgrade", "Downgrade", "None")]
    [DataRow("Uninstall", true, "Addon", "Addon", "None")]
    public void Uninstall_PlansPreviousOwnersWithoutChangingOtherOperations(
        string action, bool installed, string relation, string expectedType, string expectedState)
    {
        object bootstrapper = CreateBootstrapper(action, installed);
        object typeArgs = InvokeBootstrapperEvent(bootstrapper, "OnPlanRelatedBundleType",
            "previous-owner", relation, relation, false);
        object stateArgs = InvokeBootstrapperEvent(bootstrapper, "OnPlanRelatedBundle",
            "previous-owner", "None", "None", false);

        Assert.AreEqual(expectedType, typeArgs.GetType().GetProperty("Type")!.GetValue(typeArgs)!.ToString());
        Assert.AreEqual(expectedState, stateArgs.GetType().GetProperty("State")!.GetValue(stateArgs)!.ToString());
        object unrelatedArgs = InvokeBootstrapperEvent(bootstrapper, "OnPlanRelatedBundle",
            "unrelated-bundle", "None", "None", false);
        Assert.AreEqual("None", unrelatedArgs.GetType().GetProperty("State")!.GetValue(unrelatedArgs)!.ToString());
    }

    [TestMethod]
    public void Uninstall_WhenMsiRemovalWasSkipped_DoesNotApplyOrReportSuccess()
    {
        object bootstrapper = CreateBootstrapper("Uninstall", installed: true);
        InvokeBootstrapperEvent(bootstrapper, "OnPlannedPackage", "SimpleGit11Msi", "None", "None", false, false);
        // No engine is attached. Calling Apply here would fail the test.
        InvokeBootstrapperEvent(bootstrapper, "OnPlanComplete", 0);

        Type type = bootstrapper.GetType();
        Assert.AreEqual(unchecked((int)0x80070643), type.GetField("_exitCode", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(bootstrapper));
        Assert.IsNotNull(type.GetField("_lastError", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(bootstrapper));
    }

    [TestMethod]
    public void Uninstall_TracksOnlyActualMsiRemoval()
    {
        object bootstrapper = CreateBootstrapper("Uninstall", installed: true);
        InvokeBootstrapperEvent(bootstrapper, "OnPlannedPackage", "previous-owner", "Uninstall", "None", false, false);
        FieldInfo field = bootstrapper.GetType().GetField("_msiUninstallPlanned", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.AreEqual(false, field.GetValue(bootstrapper));

        InvokeBootstrapperEvent(bootstrapper, "OnPlannedPackage", "SimpleGit11Msi", "Uninstall", "None", false, false);
        Assert.AreEqual(true, field.GetValue(bootstrapper));
    }

    private static object CreateBootstrapper(string action, bool installed)
    {
        Type type = LoadSetupAssembly().GetType("SimpleGit11.Setup.SetupBootstrapperApplication", throwOnError: true)!;
        object bootstrapper = Activator.CreateInstance(type, nonPublic: true)!;
        FieldInfo actionField = type.GetField("_plannedAction", BindingFlags.NonPublic | BindingFlags.Instance)!;
        actionField.SetValue(bootstrapper, Enum.Parse(actionField.FieldType, action));
        type.GetField("_packageInstalled", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(bootstrapper, installed);
        return bootstrapper;
    }

    private static object InvokeBootstrapperEvent(object bootstrapper, string name, params object[] arguments)
    {
        MethodInfo method = bootstrapper.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!;
        Type eventType = method.GetParameters()[0].ParameterType;
        ConstructorInfo constructor = eventType.GetConstructors().Single();
        ParameterInfo[] parameters = constructor.GetParameters();
        object[] converted = arguments.Select((argument, index) => parameters[index].ParameterType.IsEnum
            ? Enum.Parse(parameters[index].ParameterType, (string)argument) : argument).ToArray();
        object args = constructor.Invoke(converted);
        method.Invoke(bootstrapper, [args]);
        return args;
    }

    private static Assembly LoadSetupAssembly()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SimpleGit11.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.IsNotNull(directory);
        string configuration = typeof(SetupStartupTests).Assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        string assemblyPath = Path.Combine(directory.FullName, "SimpleGit11.Setup", "bin", "x64",
            configuration, "net8.0-windows10.0.19041.0", "win-x64", "SimpleGit11.Setup.dll");
        Assembly.LoadFrom(Path.Combine(Path.GetDirectoryName(assemblyPath)!, "WixToolset.BootstrapperApplicationApi.dll"));
        return Assembly.LoadFrom(assemblyPath);
    }
}
