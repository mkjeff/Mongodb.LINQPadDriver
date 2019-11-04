using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using LINQPad.Extensibility.DataContext;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;
using MongoDB.Driver.Core.Events;

namespace MongoDB.LINQPadDriver
{
    public sealed class MongoDriver : DynamicDataContextDriver
    {
        static MongoDriver()
        {
            // Uncomment the following code to attach to Visual Studio's debugger when an exception is thrown.
            //AppDomain.CurrentDomain.FirstChanceException += (sender, args) =>
            //{
            //    if (args.Exception.StackTrace.Contains(typeof(MongoDriver).Namespace))
            //    {
            //        Debugger.Launch();
            //    }
            //};
        }

        public override string Name => "MongoDB Driver "+ Version;
        public override string Author => "mkjeff";
        public override Version Version => new Version(1, 0, 3);

        public override bool AreRepositoriesEquivalent(IConnectionInfo c1, IConnectionInfo c2)
            => c1.DatabaseInfo.CustomCxString == c2.DatabaseInfo.CustomCxString
            && c1.DatabaseInfo.Database == c2.DatabaseInfo.Database;

        public override IEnumerable<string> GetAssembliesToAdd(IConnectionInfo cxInfo)
        {
            return new[] { "*", cxInfo.CustomTypeInfo.GetAbsoluteCustomAssemblyPath() };
        }

        public override IEnumerable<string> GetNamespacesToAdd(IConnectionInfo cxInfo)
        {
            return new[]
            {
                "MongoDB.Driver",
                "MongoDB.Driver.Linq",
                cxInfo.DatabaseInfo.Server,
            };
        }

        private static readonly HashSet<string> ExcludedCommand = new HashSet<string>
        {
            "isMaster",
            "buildInfo",
            "saslStart",
            "saslContinue",
            "getLastError",
        };

        public override void InitializeContext(IConnectionInfo cxInfo, object context, QueryExecutionManager executionManager)
        {
            //Debugger.Launch();
            var mongoClientSettings = MongoClientSettings.FromUrl(new MongoUrl(cxInfo.DatabaseInfo.CustomCxString));
            mongoClientSettings.ClusterConfigurator = cb => cb
                .Subscribe<CommandStartedEvent>(e =>
                {
                    if (!ExcludedCommand.Contains(e.CommandName))
                    {
                        executionManager.SqlTranslationWriter.WriteLine(e.Command.ToJson(new JsonWriterSettings { Indent = true }));
                    }
                })
                .Subscribe<CommandSucceededEvent>(e =>
                {
                    if (!ExcludedCommand.Contains(e.CommandName))
                    {
                        executionManager.SqlTranslationWriter.WriteLine($"\t Duration = {e.Duration} \n");
                    }
                });

            var client = new MongoClient(mongoClientSettings);
            var mongoDatabase = client.GetDatabase(cxInfo.DatabaseInfo.Database);

            context.GetType().GetMethod("Initial", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(context, new[] { mongoDatabase });
        }

        public override string GetConnectionDescription(IConnectionInfo cxInfo)
            => "MongoDb - " + cxInfo.DatabaseInfo.CustomCxString + " properties";

        public override bool ShowConnectionDialog(IConnectionInfo cxInfo, ConnectionDialogOptions dialogOptions)
            => new ConnectionDialog(cxInfo).ShowDialog() == true;

        public override List<ExplorerItem> GetSchemaAndBuildAssembly(
            IConnectionInfo cxInfo, AssemblyName assemblyToBuild, ref string nameSpace, ref string typeName)
        {
            //Debugger.Launch();
            var @namespace = cxInfo.DatabaseInfo.Server;
            var types = LoadAssemblySafely(cxInfo.CustomTypeInfo.GetAbsoluteCustomAssemblyPath()).GetTypes()
                .Where(a => a.Namespace == @namespace && a.IsPublic)
                .Select(a => a.Name)
                .ToHashSet();

            var mongoClientSettings = MongoClientSettings.FromUrl(new MongoUrl(cxInfo.DatabaseInfo.CustomCxString));
            var client = new MongoClient(mongoClientSettings);
            var collections =
                (from c in client.GetDatabase(cxInfo.DatabaseInfo.Database).ListCollectionNames().ToList()
                 orderby c
                 select (collectionName: c, type: types.Contains(c) ? c : nameof(BsonDocument))
                 ).ToList();

            var source = @$"using System;
using System.Collections.Generic;
using System.Linq;
using MongoDB.Bson;
using MongoDB.Driver;
using {@namespace};

namespace {nameSpace}" +
@"{
    // The main typed data class. The user's queries subclass this, so they have easy access to all its members.
	public class " + typeName + @"
	{
        public IMongoDatabase Database => _db;
        private IMongoDatabase _db;
        internal void Initial(IMongoDatabase db)
        {
            _db = db;
        }
    
        private Lazy<IMongoCollection<T>> InitCollection<T>()
            => new Lazy<IMongoCollection<T>>(()=>_db.GetCollection<T>(typeof(T).Name));
        
        public " + typeName + @"()
        {
" + string.Join("\n", collections.Select(c =>
             $"_{c.collectionName} = InitCollection<{c.type}>();"))
+ @"}

" + string.Join("\n",
        collections.Select(c =>
        $@"private readonly Lazy<IMongoCollection<{c.type}>> _{c.collectionName};
           public IMongoCollection<{c.type}> {c.collectionName} => _{c.collectionName}.Value;"))
+ @"}	
}";

            Compile(source, assemblyToBuild.CodeBase,
                Directory.GetFiles(new FileInfo(cxInfo.CustomTypeInfo.GetAbsoluteCustomAssemblyPath()).DirectoryName, "*.dll")
                .Concat(new[]{
                    typeof(IMongoDatabase).Assembly.Location,
                    typeof(BsonDocument).Assembly.Location,
                    }));

            // We need to tell LINQPad what to display in the TreeView on the left (Schema Explorer):
            var schemas = collections.Select(a =>
                new ExplorerItem(a.collectionName, ExplorerItemKind.QueryableObject, ExplorerIcon.Table)
                {
                    IsEnumerable = true,
                    DragText = a.collectionName,
                });

            return schemas.ToList();
        }

        private static void Compile(string cSharpSourceCode, string outputFile, IEnumerable<string> customTypeAssemblyPath)
        {
            // GetCoreFxReferenceAssemblies is helper method that returns the full set of .NET Core reference assemblies.
            // (There are more than 100 of them.)
            var assembliesToReference = GetCoreFxReferenceAssemblies().Concat(customTypeAssemblyPath).ToArray();

            // CompileSource is a static helper method to compile C# source code using LINQPad's built-in Roslyn libraries.
            // If you prefer, you can add a NuGet reference to the Roslyn libraries and use them directly.
            var compileResult = CompileSource(new CompilationInput
            {
                FilePathsToReference = assembliesToReference,
                OutputPath = outputFile,
                SourceCode = new[] { cSharpSourceCode }
            });

            if (compileResult.Errors.Length > 0)
            {
                throw new Exception("Cannot compile typed context: " + compileResult.Errors[0]);
            }
        }
    }
}
