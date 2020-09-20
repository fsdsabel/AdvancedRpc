using System;
using Microsoft.Build.Framework;

namespace AdvancedRpc.MSBuild
{
    public class AdvancedRpcAot : ITask
    {
        public IBuildEngine BuildEngine { get; set; }
        public ITaskHost HostObject { get; set; }

        [Required]
        public string OutFilename { get; set; }

        [Required]
        public string[] InputFiles { get; set; }

        public bool Execute()
        {
            try
            {
                Aot.Generator.Program.Convert(new Aot.Generator.Program.Options
                {
                    Filenames = InputFiles,
                    OutFile = OutFilename,
                    Verbose = true
                });
                return true;
            }
            catch (Exception ex)
            {
                BuildEngine.LogErrorEvent(new BuildErrorEventArgs("", "-1", "", 0, 0, 0, 0, ex.ToString(), "", "", DateTime.Now));
                return false;
            }

            
            return false;
        }
    }
}
