namespace Python.EmbeddingTest
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices;

    using NUnit.Framework;
    using Python.Runtime;

    // As the SetUpFixture, the OneTimeTearDown of this class is executed after
    // all tests have run.
    [SetUpFixture]
    public partial class GlobalTestsSetup
    {
        [OneTimeSetUp]
        public void ConfigureRuntime()
        {
            if (Path.IsPathFullyQualified(Runtime.PythonDLL)) return;

            string pyDll = Environment.GetEnvironmentVariable("PYTHON_DLL_PATH");
            if (!string.IsNullOrEmpty(pyDll))
            {
                Runtime.PythonDLL = pyDll;
            }

            string pyVer = Environment.GetEnvironmentVariable("PYTHON_VERSION");
            if (!string.IsNullOrEmpty(pyVer))
            {
                Runtime.PythonVersion = Version.Parse(pyVer);
            }

            string pyHome = Environment.GetEnvironmentVariable("PYTHON_HOME")
                // defined in GitHub action setup-python
                ?? Environment.GetEnvironmentVariable("pythonLocation");
            if (!string.IsNullOrEmpty(pyHome) && !Path.IsPathFullyQualified(Runtime.PythonDLL))
            {
                string dll = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? Path.Combine(pyHome, Runtime.PythonDLL)
                    : Path.Combine(pyHome, "lib", Runtime.PythonDLL);
                if (File.Exists(dll))
                {
                    Runtime.PythonDLL = dll;
                }
            }

            if (!Path.IsPathFullyQualified(Runtime.PythonDLL))
            {
                string[] paths = Environment.GetEnvironmentVariable("PATH")
                    .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
                foreach (string pathDir in paths)
                {
                    string dll = Path.Combine(pathDir, Runtime.PythonDLL);
                    if (File.Exists(dll))
                    {
                        Runtime.PythonDLL = dll;
                        if (string.IsNullOrEmpty(pyHome))
                        {
                            pyHome = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                                // on Windows, paths is PYTHON_HOME/dll
                                ? Path.GetDirectoryName(dll)
                                // on *nix the path is HOME/lib/dll
                                : Path.GetDirectoryName(Path.GetDirectoryName(dll));
                        }

                        break;
                    }
                }
            }

            Environment.SetEnvironmentVariable("PYTHON_HOME", pyHome);
        }

        [OneTimeTearDown]
        public void FinalCleanup()
        {
            if (PythonEngine.IsInitialized)
            {
                PythonEngine.Shutdown();
            }
        }
    }
}
