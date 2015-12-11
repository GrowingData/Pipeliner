using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Data.Common;
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
using Microsoft.CodeAnalysis.Emit;

namespace GrowingData.Pipeliner {
	public class PipelineHost {

		private string _pipelinePath;
		private string _pipelineName;
		private Assembly _code;
		private Pipeline _pipeline;

		protected Logger _logger;

		private List<Assembly> _loadedAssemblies;

		public string PipelinePath { get { return _pipelinePath; } }
		public Pipeline Pipeline { get { return _pipeline; } }


		public PipelineHost(string path) {
			_pipelinePath = path;
			_pipelineName = new DirectoryInfo(path).Name;

			_logger = LogManager.GetLogger("PipelineHost");
		}

		public bool Load() {
			var assembly = GetAssembly();
			if (assembly == null) {
				return false;
			}

			var pipeline = LoadType(assembly);
			if (pipeline == null) {
				return false;
			}

			_pipeline = pipeline;
			return true;

		}

		private List<string> GetBaseAssemblies() {
			var assemblies = new List<string>();
			assemblies.Add(typeof(object).Assembly.Location);
			assemblies.Add(typeof(Enumerable).Assembly.Location);
			assemblies.Add(typeof(Pipeline).Assembly.Location);
			assemblies.Add(typeof(DbConnection).Assembly.Location);
			assemblies.Add(typeof(SqlConnection).Assembly.Location);

			//foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) {
			//	assemblies.Add(a.Location);
			//}
			return assemblies;
		}

		private List<string> GetExplicitAssemblies() {
			var assembliesListLocation = Path.Combine(PipelinePath, "assemblies.txt");
			var explicitAssemblyReferences = new List<string>();
			if (File.Exists(assembliesListLocation)) {
				foreach (var line in File.ReadAllLines(assembliesListLocation).Select(x => x.Trim())) {
					if (line.EndsWith(".dll")) {
						explicitAssemblyReferences.Add(line);
					}
				}
			}
			return explicitAssemblyReferences;
		}

		private List<string> SourceFiles() {
			return Directory.GetFiles(PipelinePath, "*.cs").ToList();
		}

		private DateTime LastUpdated(List<string> files) {
			return files.Select(x => new FileInfo(x).LastWriteTime).Max();

		}

		private string PipelineAssemblyPath {
			get {
				return Path.Combine(_pipelinePath, _pipelineName + ".dll");
			}
		}


		private Assembly GetAssembly() {
			var csFiles = SourceFiles();
			var lastCodeModification = LastUpdated(csFiles);


			var explicitReferences = GetExplicitAssemblies();

			if (File.Exists(PipelineAssemblyPath)) {
				var lastCompile = new FileInfo(PipelineAssemblyPath).LastWriteTime;
				if (lastCompile < lastCodeModification) {
					if (!CompileAssembly(explicitReferences)) {
						return null;
					}
				}
			} else {
				if (!CompileAssembly(explicitReferences)) {
					return null;
				}
			}

			return LoadAssembly(explicitReferences);

		}

		private Pipeline LoadType(Assembly assembly) {


			// Verify that the assembly has a single entrypoint
			var pipelines = assembly.GetTypes()
				.Where(t => t.BaseType.FullName == "GrowingData.Pipeliner.Pipeline")
				.ToList();

			if (pipelines.Count > 1) {
				_logger.Error(string.Format("This pipeline contains more than one Pipeline object, {0}\r\n\r\nPipeline: {1}",
					string.Join("\r\n", pipelines.Select(x => x.Name)),
					PipelinePath)
				);
				return null;
			}
			if (pipelines.Count == 0) {
				_logger.Error(string.Format("No class inheriting from Pipeline was found in: \r\n{0}",
						PipelinePath)
					);
				return null;
			}
			_code = assembly;
			var type = pipelines.First();

			return Activator.CreateInstance(type, _pipelinePath) as Pipeline;
		}


		private Assembly LoadAssembly(List<string> explicitAssemblyReferences) {
			AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;

			// Make sure that all the dependent assemblies are loaded
			_loadedAssemblies = new List<Assembly>();
			// Load all the referenced assemblies prior to loading our assembly
			foreach (var assemblyPath in explicitAssemblyReferences) {
				_loadedAssemblies.Add(Assembly.LoadFile(assemblyPath));
			}


			var assembly = Assembly.LoadFile(PipelineAssemblyPath);
			return assembly;


		}

		private bool CompileAssembly(List<string> explicitAssemblyReferences) {
			// Use the current path yo...

			var csFiles = SourceFiles();

			var trees = new List<SyntaxTree>();

			foreach (var csFile in csFiles) {
				var source = File.ReadAllText(csFile);
				var syntax = CSharpSyntaxTree.ParseText(source);

				trees.Add(syntax);
			}

			MetadataReference[] references = GetCompilationAssemblies();

			CSharpCompilation compilation = CSharpCompilation.Create(
				_pipelineName + ".dll",
				syntaxTrees: trees.ToList(),
				references: references,
				options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, platform: Platform.X64)
			);

			EmitResult result = compilation.Emit(PipelineAssemblyPath);

			if (!result.Success) {
				IEnumerable<Diagnostic> failures = result.Diagnostics.Where(diagnostic =>
					diagnostic.IsWarningAsError ||
					diagnostic.Severity == DiagnosticSeverity.Error);

				foreach (Diagnostic diagnostic in failures) {
					_logger.Error(string.Format("Compile Error for {2}\r\n: {0}: {1}", diagnostic.Id, diagnostic.GetMessage(), PipelinePath));
				}

				if (File.Exists(PipelineAssemblyPath)) {
					File.Delete(PipelineAssemblyPath);
				}
				return false;
			} else {
				return true;
			}

		}

		private MetadataReference[] GetCompilationAssemblies() {

			var assemblies = GetBaseAssemblies()
				.Union(GetExplicitAssemblies());

			var missingAssemblies = assemblies.Where(x => !File.Exists(x)).ToList();

			if (missingAssemblies.Count > 0) {
				foreach (var missingAssembly in missingAssemblies) {
					_logger.Error(string.Format("Unable to load assembly '{0}' in Pipeline: {1}", missingAssembly, _pipelinePath));
				}
			}

			_logger.Info(string.Format("Loading assemblies:"));
			foreach (var a in assemblies) {
				_logger.Info(string.Format("\t{0}", a));
			}

			MetadataReference[] references = assemblies
				.Distinct()
				.Select(x => MetadataReference.CreateFromFile(x))
				.ToArray();

			return references;
		}

		private Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args) {
			var loaded = _loadedAssemblies.FirstOrDefault(x => x.FullName == args.Name);

			if (loaded == null) {
				throw new Exception(string.Format("Unable to load Assembly: {0} required for Pipeline: {1}, please add a reference to 'assemblies.txt' in the root of the Pipeline",
				args.Name, _pipelineName));
			}
			return loaded;
		}
	}
}
