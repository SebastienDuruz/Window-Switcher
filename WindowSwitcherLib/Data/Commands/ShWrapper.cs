using System;
using System.Diagnostics;

namespace WindowSwitcherLib.Data.Commands;

public class ShWrapper() : CommandBase("sh"), ICommandWrapper
{
    public string Execute(string args)
    {
        ArgumentNullException.ThrowIfNull(args);

        using Process process = CreateProcess();
        process.StartInfo.ArgumentList.Clear();
        process.StartInfo.ArgumentList.Add("-lc");
        process.StartInfo.ArgumentList.Add(args);
        
        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        return output;
    }
}
