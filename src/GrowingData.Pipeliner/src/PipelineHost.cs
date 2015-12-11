using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using GrowingData.Utilities;
using NLog;
using NLog.Targets;
using System.Reflection;
using System.Text.RegularExpressions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Syntax;
using Microsoft.CodeAnalysis.CSharp;

namespace GrowingData.Pipeliner {
	public class PipelineHost {

		private string _pipelinePath;
		private string _pipelineName;
		private Assembly _code;
		private Pipeline _pipeline;

		protected Logger _logger;


		public string PipelinePath { get { return _pipelinePath; } }
		public Pipeline Pipeline { get { return _pipeline; } }


		public PipelineHost(string path) {
			_pipelinePath = path;
			_pipelineName = new DirectoryInfo(path).Name;

			_logger = LogManager.GetLogger("PipelineHost");
		}


		public bool Compile() {
			// Use the current path yo...

			var csFiles = Directory.GetFiles(PipelinePath, "*.cs");

			var trees = new List<SyntaxTree>();
			foreach (var csFile in csFiles) {
				var source = File.ReadAllText(csFile);
				var syntax = CSharpSyntaxTree.ParseText(source);

				trees.Add(syntax);
			}
			var assembliesListLocation = Path.Combine(PipelinePath, "assemblies.txt");

			var assemblies = new List<string>();
			assemblies.Add(typeof(object).Assembly.Location);
			assemblies.Add(typeof(Enumerable).Assembly.Location);

			foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) {
				assemblies.Add(a.Location);
			}

			if (File.Exists(assembliesListLocation)) {
				foreach (var line in File.ReadAllLines(assembliesListLocation).Select(x => x.Trim())) {
					if (line.EndsWith(".dll")) {
						assemblies.Add(line);
					}
				}
			}
			var missingAssemblies = assemblies.Where(x => !File.Exists(x)).ToList();

			if (missingAssemblies.Count > 0) {
				foreach (var missingAssembly in missingAssemblies) {
					_logger.Error(string.Format("Unable to load assembly '{0}' in Pipeline: {1}", missingAssembly, _pipelinePath));
				}
				return false;
			}

			_logger.Info(string.Format("Loading assemblies:"));
			foreach (var a in assemblies) {
				_logger.Info(string.Format("\t{0}", a));
			}

			MetadataReference[] references = assemblies
				.Distinct()
				.Select(x => MetadataReference.CreateFromFile(x))
				.ToArray();

			CSharpCompilation compilation = null;
			string assemblyName = Path.GetRandomFileName();

			compilation = CSharpCompilation.Create(
				assemblyName,
				syntaxTrees: trees.ToList(),
				references: references,
				options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
			);

			using (var ms = new MemoryStream()) {
				var result = compilation.Emit(ms);

				if (!result.Success) {
					IEnumerable<Diagnostic> failures = result.Diagnostics.Where(diagnostic =>
						diagnostic.IsWarningAsError ||
						diagnostic.Severity == DiagnosticSeverity.Error);

					foreach (Diagnostic diagnostic in failures) {
						_logger.Error(string.Format("Compile Error for {2}\r\n: {0}: {1}", diagnostic.Id, diagnostic.GetMessage(), PipelinePath));
					}
					return false;
				} else {
					ms.Seek(0, SeekOrigin.Begin);
					var assembly = Assembly.Load(ms.ToArray());

					// Verify that the assembly has a single entrypoint
					var pipelines = assembly.GetTypes()
						.Where(t => t.BaseType.FullName == "GrowingData.Pipeliner.Pipeline")
						.ToList();

					if (pipelines.Count > 1) {
						_logger.Error(string.Format("This pipeline contains more than one Pipeline object, {0}\r\n\r\nPipeline: {1}",
							string.Join("\r\n", pipelines.Select(x => x.Name)),
							PipelinePath)
						);
						return false;
					}
					if (pipelines.Count == 0) {
						_logger.Error(string.Format("No class inheriting from Pipeline was found in: \r\n{0}",
								PipelinePath)
							);
						return false;
					}
					_code = assembly;
					var type = pipelines.First();
					_pipeline = Activator.CreateInstance(type, _pipelinePath) as Pipeline;
					return true;
				}
			}
		}
	}
}
