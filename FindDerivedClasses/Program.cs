using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;

using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.CSharp.ProjectDecompiler;
using ICSharpCode.Decompiler.DebugInfo;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;
using ICSharpCode.ILSpyX.PdbProvider;

using McMaster.Extensions.CommandLineUtils;

namespace ICSharpCode.ILSpyCmd
{
	[Command(Name = "FindDerivedClasses", Description = "Find all classes derivated from a specified base class",
		ExtendedHelpText = @"
Examples:
   FindDerivedClasses path/to/my/assemblies.*.dll -o another/path/to/output/file.txt -t _my.namespace.myType
")]
	[HelpOption("-h|--help")]
	[ProjectOptionRequiresOutputDirectoryValidation]
	[VersionOptionFromMember("-v|--version", Description = "Show version of ICSharpCode.Decompiler used.",
		MemberName = nameof(DecompilerVersion))]
	class ILSpyCmdProgram
	{
		public static int Main(string[] args) => CommandLineApplication.Execute<ILSpyCmdProgram>(args);

		[Required]
		[Argument(0, "Assembly file location", "Location of assembly or assemblies, wildcard permitted. This argument is mandatory.")]
		public string InputAssemblyLocation { get; }

		[Required]
		[Option("-o|--outputFile <location.txt>", "Output location of text file.", CommandOptionType.SingleValue)]
		public string OutputFileLocation { get; }

		[Option("-p|--project", "Decompile assembly as compilable project. This requires the output directory option.", CommandOptionType.NoValue)]
		public bool CreateCompilableProjectFlag { get; }

		[Required]
		[Option("-t|--type <type-name>", "The fully qualified name of the reference base type.", CommandOptionType.SingleValue)]
		public string ReferenceTypeName { get; }
		
		[Option("--fullname", "Output fully qualified names.", CommandOptionType.NoValue)]
		public bool OutputFullyQualifiedName { get; }
		
		//

		public string DecompilerVersion => "ilspycmd: " + typeof(ILSpyCmdProgram).Assembly.GetName().Version.ToString() +
				Environment.NewLine
				+ "ICSharpCode.Decompiler: " +
				typeof(FullTypeName).Assembly.GetName().Version.ToString();

		[Option("-lv|--languageversion <version>", "C# Language version: CSharp1, CSharp2, CSharp3, " +
			"CSharp4, CSharp5, CSharp6, CSharp7, CSharp7_1, CSharp7_2, CSharp7_3, CSharp8_0, CSharp9_0, " +
			"CSharp10_0, Preview or Latest", CommandOptionType.SingleValue)]
		public LanguageVersion LanguageVersion { get; } = LanguageVersion.Latest;

		[DirectoryExists]
		[Option("-r|--referencepath <path>", "Path to a directory containing dependencies of the assembly that is being decompiled.", CommandOptionType.MultipleValue)]
		public string[] ReferencePaths { get; } = new string[0];

		[Option("--no-dead-code", "Remove dead code.", CommandOptionType.NoValue)]
		public bool RemoveDeadCode { get; }

		[Option("--no-dead-stores", "Remove dead stores.", CommandOptionType.NoValue)]
		public bool RemoveDeadStores { get; }

		[Option("--nested-directories", "Use nested directories for namespaces.", CommandOptionType.NoValue)]
		public bool NestedDirectories { get; }
		
		[FileExistsOrNull]
		[Option("-usepdb|--use-varnames-from-pdb", "Use variable names from PDB.", CommandOptionType.SingleOrNoValue)]
		public (bool IsSet, string Value) InputPDBFile { get; }

		private int OnExecute(CommandLineApplication app)
		{
			var assemblyFiles = Directory.GetFiles(Path.GetDirectoryName(InputAssemblyLocation), Path.GetFileName(InputAssemblyLocation));
			List<IType> matchedTypes = new();
			foreach (var file in assemblyFiles)
			{
				try
				{
					matchedTypes.AddRange(ListMatchedClasses(file, ReferenceTypeName));
				}
				catch(PEFileNotSupportedException)
				{
				}
			}

			try
			{
				Console.WriteLine($"Found {matchedTypes.Count} derived classes.");
				File.WriteAllLines(OutputFileLocation, matchedTypes.Select(x => OutputFullyQualifiedName ? x.FullName : x.Name).ToList());
			}
			catch (Exception e)
			{
				Console.WriteLine("Error while writing text file: " + e);
			}

			return 0;
		}

		private IEnumerable<IType> ListMatchedClasses(string assemblyFile, string targetBaseClassFullName)
		{
			CSharpDecompiler decompiler = GetDecompiler(assemblyFile);
			return decompiler.TypeSystem.MainModule.TypeDefinitions.Where(x => MatchBaseClass(x, targetBaseClassFullName));
		}
		
		private static bool MatchBaseClass(IType type, string baseClassFullName)
		{
			foreach (var directBaseType in type.DirectBaseTypes)
			{
				if(directBaseType.FullName == baseClassFullName)
					return true;
				if(MatchBaseClass(directBaseType, baseClassFullName))
					return true;
			}
			return false;
		}

		private DecompilerSettings GetSettings(PEFile module)
		{
			return new DecompilerSettings(LanguageVersion) {
				ThrowOnAssemblyResolveErrors = false,
				RemoveDeadCode = RemoveDeadCode,
				RemoveDeadStores = RemoveDeadStores,
				UseSdkStyleProjectFormat = WholeProjectDecompiler.CanUseSdkStyleProjectFormat(module),
				UseNestedDirectoriesForNamespaces = NestedDirectories,
			};
		}

		private CSharpDecompiler GetDecompiler(string assemblyFileName)
		{
			var module = new PEFile(assemblyFileName);
			var resolver = new UniversalAssemblyResolver(assemblyFileName, false, module.Metadata.DetectTargetFrameworkId());
			foreach (var path in ReferencePaths)
			{
				resolver.AddSearchDirectory(path);
			}
			return new CSharpDecompiler(assemblyFileName, resolver, GetSettings(module)) {
				DebugInfoProvider = TryLoadPDB(module)
			};
		}

		private IDebugInfoProvider TryLoadPDB(PEFile module)
		{
			if (InputPDBFile.IsSet)
			{
				if (InputPDBFile.Value == null)
					return DebugInfoUtils.LoadSymbols(module);
				return DebugInfoUtils.FromFile(module, InputPDBFile.Value);
			}

			return null;
		}
	}
}
