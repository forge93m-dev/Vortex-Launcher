using System;
using System.IO;
using System.Linq;

namespace VortexLauncher.Core.Launch;

public static class JavaFinder
{
    public static string GetJavaPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            foreach (string version in new[] { "26.1", "1.21", "1.20.1" })
            {
                string java = JavaResolver.GetJavaForVersion(version);
                if (File.Exists(java)) return java;
            }
            return PlatformSupport.JavaExecutable;
        }
        string[] searchPaths = new[]
        {
            @"C:\Program Files\Java",
            @"C:\Program Files (x86)\Java",
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Programs\Eclipse Adoptium",
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + @"\Eclipse Adoptium"
        };

        foreach (var basePath in searchPaths)
        {
            if (Directory.Exists(basePath))
            {
                // Ищем JDK 25 (класс 69.0) или JDK 21, исключая старые Java 8 и экспериментальную 26
                var javawFiles = Directory.GetFiles(basePath, "javaw.exe", SearchOption.AllDirectories)
                    .Where(f => !f.Contains("1.8") && !f.Contains("jre8") && !f.Contains("jdk8") && !f.Contains("jdk-26"))
                    .OrderByDescending(f => f)
                    .ToList();

                if (javawFiles.Count > 0)
                {
                    return javawFiles[0];
                }
            }
        }

        string? javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrEmpty(javaHome) && !javaHome.Contains("1.8") && !javaHome.Contains("26"))
        {
            string javaw = Path.Combine(javaHome, "bin", "javaw.exe");
            if (File.Exists(javaw)) return javaw;
        }

        return "javaw.exe";
    }
}
