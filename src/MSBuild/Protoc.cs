namespace Google.ProtocolBuffers.MsBuild
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text;
    using DescriptorProtos;
    using Microsoft.Build.Tasks;
    using ProtoGen;
    using Microsoft.Build.Framework;
    using Microsoft.Build.Utilities;

    public class Protoc : Task
    {
        private readonly string workDir = Guid.NewGuid().ToString();
        
        /// <summary>
        /// Gets or sets the protos.
        /// </summary>
        /// <value>The protos.</value>
        [Required]
        public ITaskItem[] Protos { get; set; }

        /// <summary>
        /// Gets or sets the output directory.
        /// </summary>
        /// <value>The output directory.</value>
        [Required]
        public string Output { get; set; }

        private string protoc = "protoc.exe";

        /// <summary>
        /// Gets or sets the path to protoc.exe.  If not defined, then it assumes it is one the path.
        /// </summary>
        /// <value>The protoc.</value>
        public string ProtocExe
        {
            get { return protoc; }
            set { protoc = value; }
        }

        /// <summary>
        /// Gets or sets the path to the protocol buffer files to be included
        /// </summary>
        /// <value>The proto path.</value>
        public string ProtoPath { get; set; }

        /// <summary>
        /// Gets or sets the im
        /// </summary>
        /// <value>The include imports.</value>
        public bool IncludeImports { get; set; }

        /// <summary>
        /// Gets or sets the name of the assembly.
        /// </summary>
        /// <value>The name of the assembly.</value>
        public ITaskItem OutputAssembly { get; set; }

        /// <summary>
        /// Gets or sets the assembly refences.
        /// </summary>
        /// <value>The assembly refences.</value>
        public ITaskItem[] AssemblyReferences { get; set; }

        /// <summary>
        /// Gets or sets the key file.
        /// </summary>
        /// <value>The key file.</value>
        public string KeyFile { get; set; }

        private string tempDir = Path.GetTempPath();

        /// <summary>
        /// Gets or sets the temporary directory to store the protobin files.  This 
        /// will use the default temp dir if not specified
        /// </summary>
        /// <value>The temp dir.</value>
        public string TempDir
        {
            get { return tempDir; }
            set { tempDir = value; }
        }

        /// <summary>
        /// When overridden in a derived class, executes the task.
        /// </summary>
        /// <returns>
        /// true if the task successfully executed; otherwise, false.
        /// </returns>
        public override bool Execute()
        {
            if (!File.Exists(ProtocExe))
            {
                Log.LogError(string.Format("Could not find protoc.exe at path '{0}'", ProtocExe));
            }

            if (!Directory.Exists(tempDir))
            {
                Log.LogError(string.Format("Temporary directory does not exist '{0}'", TempDir));
                return false;
            }
            Directory.CreateDirectory(GetTempDir());

            if (!Directory.Exists(Output))
            {
                Log.LogError(string.Format("Output directory does not exist '{0}'", Output));
                return false;
            }

            if (Protos.Length < 1)
            {
                Log.LogError(string.Format("No input files where specified"));
                return false;
            }

            var files = Protos.Select(i => new FileInfo(i.ItemSpec)).ToArray();
            foreach (var file in files.Where(file => !file.Exists))
            {
                Log.LogError(string.Format("Could not find input file '{0}'", file.Name));
                return false;
            }

            if (!ExecuteProtoc(files) && GenerateProtos()) return false;

            if (OutputAssembly != null)
            {
                var compile = new Csc
                {
                    BuildEngine = BuildEngine,
                    OutputAssembly = OutputAssembly,
                    References = AssemblyReferences,
                    Sources = Directory.GetFiles(Output, "*.cs").Select(f => new TaskItem(f)).ToArray(),
                    DebugType = "pdbonly",
                    WarningLevel = 1,
                    TargetType = "library",
                    KeyFile = KeyFile
                };
                return compile.Execute();
            }

            return true;
        }

        private bool ExecuteProtoc(IEnumerable<FileInfo> files)
        {
            var options = BuildOptions(files);
            Log.LogCommandLine(ProtocExe + " " + options);
            var process = Process.Start(new ProcessStartInfo(ProtocExe, options)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            });
            process.WaitForExit(30000);
            var stderr = process.StandardError.ReadToEnd();
            var stdout = process.StandardOutput.ReadToEnd();
            Log.LogCommandLine(stdout);
            var exit = process.ExitCode;
            if (exit > 0)
            {
                Log.LogError(stderr);
            }
            process.Close();
            return exit == 0;
        }

        private string BuildOptions(IEnumerable<FileInfo> files)
        {
            var builder = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(ProtoPath))
                builder.Append("--proto_path=").Append(ProtoPath).Append(" ");

            if (IncludeImports)
                builder.Append("--include_imports ");

            builder.Append("--descriptor_set_out=").Append(GetDescriptor()).Append(" ");

            foreach (var file in files)
                builder.Append(file).Append(" ");

            return builder.ToString();
        }

        private bool GenerateProtos()
        {
            DescriptorProtoFile.Descriptor.ToString();
            var options = new GeneratorOptions
            {
                InputFiles = new List<string>(new[] { GetDescriptor() }),
                OutputDirectory = Output
            };

            IList<string> validationFailures;
            if (!options.TryValidate(out validationFailures))
            {
                var exception = new InvalidOptionsException(validationFailures);
                Log.LogError(string.Format("Invalid options: " + exception.Message));
                return false;
            }

            var generator = Generator.CreateGenerator(options);
            generator.Generate();

            return true;
        }

        public string GetTempDir()
        {
            return Path.Combine(tempDir, workDir);
        }

        public string GetDescriptor()
        {
            return Path.Combine(GetTempDir(), new FileInfo(Protos.First().ItemSpec).Name);
        }
    }
}
