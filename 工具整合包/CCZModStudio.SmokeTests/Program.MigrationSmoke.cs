using CCZModStudio.Core;
using CCZModStudio.Formats;
using CCZModStudio.Models;
using CCZModStudio;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

internal partial class Program
{
    static void RunMigrationSmoke(CczProject sourceProject)
    {
        var migrationRoot = Path.Combine(Path.GetTempPath(), "CCZModStudio_MigrationSmoke_" + Guid.NewGuid().ToString("N"));
        var movedGameRoot = Path.Combine(migrationRoot, "MovedGame");
    
        try
        {
            Directory.CreateDirectory(movedGameRoot);
            foreach (var coreFile in new[] { "Ekd5.exe", "Data.e5", "Star.e5", "Imsg.e5" })
            {
                var source = Path.Combine(sourceProject.GameRoot, coreFile);
                if (!File.Exists(source))
                {
                    throw new FileNotFoundException("Migration smoke missing source core file.", source);
                }
    
                File.Copy(source, Path.Combine(movedGameRoot, coreFile), overwrite: false);
            }
    
            var rsRoot = Path.Combine(movedGameRoot, "RS");
            Directory.CreateDirectory(rsRoot);
            var sourceScenarioPath = Path.Combine(sourceProject.GameRoot, "RS", "R_00.eex");
            if (!File.Exists(sourceScenarioPath))
            {
                sourceScenarioPath = Directory.GetFiles(Path.Combine(sourceProject.GameRoot, "RS"), "R_*.eex", SearchOption.TopDirectoryOnly)
                    .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
                    .FirstOrDefault()
                    ?? throw new FileNotFoundException("Migration smoke cannot find R_*.eex.", Path.Combine(sourceProject.GameRoot, "RS"));
            }
    
            File.Copy(sourceScenarioPath, Path.Combine(rsRoot, Path.GetFileName(sourceScenarioPath)), overwrite: false);
    
            var migratedProject = new ProjectDetector().CreateProjectFromGameRoot(movedGameRoot);
            if (!migratedProject.GameRoot.Equals(movedGameRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Migration smoke resolved an unexpected game root: " + migratedProject.GameRoot);
            }
    
            if (!File.Exists(migratedProject.HexTableXmlPath))
            {
                throw new FileNotFoundException(ProjectDetector.BuildMissingHexTableMessage(migratedProject), migratedProject.HexTableXmlPath);
            }
    
            var migratedTables = new HexTableParser().Load(migratedProject.HexTableXmlPath);
            if (migratedTables.Count == 0)
            {
                throw new InvalidOperationException("Migration smoke loaded zero HexTable definitions.");
            }
    
            var sceneStringPath = ProjectDetector.FindSceneDictionaryPath(migratedProject);
            if (!File.Exists(sceneStringPath))
            {
                throw new FileNotFoundException("Migration smoke cannot relocate CczString.ini.", sceneStringPath);
            }
    
            if (!string.IsNullOrWhiteSpace(migratedProject.SceneEditorDirectory) &&
                !Directory.Exists(migratedProject.SceneEditorDirectory))
            {
                throw new DirectoryNotFoundException("Migration smoke resolved a stale scene editor directory: " + migratedProject.SceneEditorDirectory);
            }
    
            if (!string.IsNullOrWhiteSpace(migratedProject.ImageAssignerDirectory) &&
                !Directory.Exists(migratedProject.ImageAssignerDirectory))
            {
                throw new DirectoryNotFoundException("Migration smoke resolved a stale image assigner directory: " + migratedProject.ImageAssignerDirectory);
            }
    
            if (!string.IsNullOrWhiteSpace(migratedProject.ImageAssignerSystemIniPath) &&
                !File.Exists(migratedProject.ImageAssignerSystemIniPath))
            {
                throw new FileNotFoundException("Migration smoke resolved a stale image assigner System.ini.", migratedProject.ImageAssignerSystemIniPath);
            }
    
            if (!string.IsNullOrWhiteSpace(migratedProject.MaterialLibraryRoot) &&
                !Directory.Exists(migratedProject.MaterialLibraryRoot))
            {
                throw new DirectoryNotFoundException("Migration smoke resolved a stale material library root: " + migratedProject.MaterialLibraryRoot);
            }
    
            if (!string.IsNullOrWhiteSpace(migratedProject.PatchConfigRoot) &&
                !Directory.Exists(migratedProject.PatchConfigRoot))
            {
                throw new DirectoryNotFoundException("Migration smoke resolved a stale patch config root: " + migratedProject.PatchConfigRoot);
            }
    
            var materialAssets = new MaterialLibraryIndexer().Index(migratedProject);
            var scenarioIndex = new ScenarioFileReader().ReadAllIndex(migratedProject);
            if (scenarioIndex.Count != 1 || !ScenarioFileReader.IsRsScriptFile(scenarioIndex[0].FileName))
            {
                throw new InvalidOperationException("Migration smoke failed to read the moved R/S scenario index.");
            }
    
            Console.WriteLine($"MIGRATION_SMOKE OK movedRoot={movedGameRoot} workspace={migratedProject.WorkspaceRoot} hex={migratedProject.HexTableXmlPath} dict={sceneStringPath} sceneEditor={migratedProject.SceneEditorDirectory ?? "<missing>"} imageAssigner={migratedProject.ImageAssignerDirectory ?? "<missing>"} materialRoot={migratedProject.MaterialLibraryRoot ?? "<missing>"} patchRoot={migratedProject.PatchConfigRoot ?? "<missing>"} tables={migratedTables.Count} scenarios={scenarioIndex.Count} materials={materialAssets.Count}");
        }
        finally
        {
            try
            {
                if (Directory.Exists(migrationRoot)) Directory.Delete(migrationRoot, recursive: true);
            }
            catch
            {
                // The temp copy is non-authoritative test data; a later cleanup can remove it if Windows still has a handle open.
            }
        }
    }
}
