using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace TpacTool.Lib
{
    public class AssetManager : IDependenceResolver
    {
        [CanBeNull]
        public DirectoryInfo WorkDir { set; get; }

        private List<AssetPackage> _loadedPackages;

        private List<AssetItem> _loadedAssets;

        private Dictionary<Guid, AssetPackage> _packageLookup;

        private Dictionary<Guid, AssetItem> _assetLookup;

#if NET40
		public List<AssetPackage> LoadedPackages { private set; get; }

		public List<AssetItem> LoadedAssets { private set; get; }
#else
        public IReadOnlyList<AssetPackage> LoadedPackages { private set; get; }

        public IReadOnlyList<AssetItem> LoadedAssets { private set; get; }
#endif

        //public Dictionary<Guid, AbstractExternalLoader> FixedExternalData { private set; get; }

        // return true for continuing. false for halt
        public delegate bool ProgressCallback(int package, int packageCount, string fileName, bool completed);

        public AssetManager()
        {
            _loadedPackages = new List<AssetPackage>();
            _loadedAssets = new List<AssetItem>();
            _packageLookup = new Dictionary<Guid, AssetPackage>();
            _assetLookup = new Dictionary<Guid, AssetItem>();
#if NET40
			LoadedPackages = _loadedPackages;
			LoadedAssets = _loadedAssets;
#else
            LoadedPackages = _loadedPackages.AsReadOnly();
            LoadedAssets = _loadedAssets.AsReadOnly();
#endif
            //FixedExternalData = new Dictionary<Guid, AbstractExternalLoader>();
        }

        public virtual void Load(DirectoryInfo assetDir)
        {
            if (!assetDir.Exists)
                throw new FileNotFoundException("Asser folder not exists: " + assetDir.FullName);
            WorkDir = assetDir;
            LoadDo();
        }

        public virtual void Load(DirectoryInfo assetDir, ProgressCallback callback)
        {
            if (!assetDir.Exists)
                throw new FileNotFoundException("Asser folder not exists: " + assetDir.FullName);
            WorkDir = assetDir;
#if NETSTANDARD1_3
            LoadDo(callback);
#else
			Thread thread = new Thread(() =>
			{
				LoadDo(callback);
			});
			thread.Name = "Asset Loader";
			thread.IsBackground = true;
			thread.Start();
#endif
        }

        protected virtual void LoadDo(ProgressCallback callback = null)
        {
            bool reportProgress = callback != null;
            List<FileInfo> files = null;
#if !NETSTANDARD1_3
			Thread thread = new Thread(() =>
			{
				try
				{
					var fs = WorkDir.EnumerateFiles("*.tpac", SearchOption.AllDirectories).ToList();
					files = fs;
				}
				catch (ThreadAbortException)
				{
				}
			});
			thread.Name = "Tpac Searcher";
			thread.IsBackground = true;
			thread.Start();
			while (true)
			{
				if (reportProgress && !callback(-1, -1, String.Empty, false))
				{
					if (thread.ThreadState != ThreadState.Stopped)
						thread.Abort();
					InterruptLoading();
					return;
				}
				if (files != null)
					break;
				Thread.Sleep(100);
				Thread.Yield();
			}
#else
            files = WorkDir.EnumerateFiles("*.tpac", SearchOption.AllDirectories).ToList();
#endif

            var packageCount = files.Count;
            int i = 0;
            foreach (var file in files)
            {
                if (reportProgress && !callback(i++, packageCount, file.Name, false))
                {
                    InterruptLoading();
                    return;
                }
                var package = new AssetPackage(file.FullName);
                package.IsGuidLocked = true;
                _loadedPackages.Add(package);
                _packageLookup[package.Guid] = package;
                foreach (var assetItem in package.Items)
                {
                    _assetLookup[assetItem.Guid] = assetItem;
                    _loadedAssets.Add(assetItem);
                }
            }

            if (reportProgress && !callback(packageCount, packageCount, String.Empty, true))
            {
                InterruptLoading();
                return;
            }
        }

        protected virtual void InterruptLoading()
        {
            _loadedPackages.Clear();
            _loadedAssets.Clear();
            _packageLookup.Clear();
            _assetLookup.Clear();
        }

        public object this[Guid guid]
        {
            get
            {
                if (_packageLookup.TryGetValue(guid, out var result1))
                    return result1;
                if (_assetLookup.TryGetValue(guid, out var result2))
                    return result2;
                //if (FixedExternalData.TryGetValue(guid, out var result3))
                //	return result3;
                return null;
            }
        }

        public AssetPackage GetPackage(Guid guid)
        {
            _packageLookup.TryGetValue(guid, out var result);
            return result;
        }

        [Obsolete] // WIP. adding assert lookup is not finished yet
        public void AddPackage(AssetPackage package)
        {
            if (package.IsGuidLocked)
                throw new ArgumentException("The guid of adding package is locked. That is unexpected. " +
                                    "It can be caused by adding an existed package to its owner manager", "package");
            if (_packageLookup.ContainsKey(package.Guid))
                throw new ArgumentException("The guid is already occupied: " + package.Guid, "package");
            if (package.Guid == Guid.Empty)
                throw new ArgumentException("The guid cannot be empty", "package");
            _loadedPackages.Add(package);
            _packageLookup[package.Guid] = package;
            package.IsGuidLocked = true;
        }

        [Obsolete] // WIP. removing assert lookup is not finished yet
        public bool RemovePackage(Guid guid, out AssetPackage removedPackage)
        {
            if (_packageLookup.TryGetValue(guid, out removedPackage))
            {
                _packageLookup.Remove(guid);
                _loadedPackages.Remove(removedPackage);
                return true;
            }

            return false;
        }

        public void exportPackage()
        {
            HashSet<Guid> missedMatGuids = new HashSet<Guid>();
            string missedMatsCSV = Path.Combine(WorkDir.FullName + "\\missedMatGuids.csv");
            if (File.Exists(missedMatsCSV))
            {
                var reader = new StreamReader(File.OpenRead(missedMatsCSV));
                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    //var values = line.Split(';');
                    if (line != "")
                        missedMatGuids.Add(Guid.Parse(line));
                }
            }

            //scan missed materials and textures.
            if (missedMatGuids.Count == 0)
            {
                foreach (var package in _loadedPackages)
                {
                    foreach (var assetItem in package.Items)
                    {
                        if (assetItem.Type == Metamesh.TYPE_GUID)
                        {
                            var metamesh = assetItem as Metamesh;
                            foreach (var mesh in metamesh.Meshes)
                            {
                                if (mesh.Lod > 0)
                                    continue;

                                if (mesh.Material.Guid != Guid.Empty)
                                {
                                    if (!mesh.Material.TryGetItem(out var mat1))
                                    {
                                        missedMatGuids.Add(mesh.Material.Guid);
                                    }
                                }
                                else if (mesh.SecondMaterial.Guid != Guid.Empty)
                                {
                                    if (!mesh.SecondMaterial.TryGetItem(out var mat2))
                                    {
                                        missedMatGuids.Add(mesh.SecondMaterial.Guid);
                                    }
                                }
                            }
                        }
                    }
                }

                if (missedMatGuids.Count > 0)
                {
                    string path = Path.Combine(WorkDir.FullName + "\\missedMatGuids.csv");
                    string s = "";
                    //s += this.Items.Count.ToString();
                    foreach (var guid in missedMatGuids)
                    {
                        s += guid;
                        s += "\r\n";
                    }

                    System.IO.File.WriteAllText(path, s);
                }
            }
            else
            {
                HashSet<Guid> missedTexGuids = new HashSet<Guid>();
                foreach (var guid in missedMatGuids)
                {
                    if (_assetLookup.TryGetValue(guid, out var result))
                    {
                        var mat = result as Material;
                        foreach (var texDep in mat.Textures.Values)
                        {
                            if (texDep.TryGetItem(out var tex))
                                missedTexGuids.Add(tex.Guid);
                        }
                    }
                }

                if (missedTexGuids.Count > 0 || missedMatGuids.Count > 0)
                {
                    foreach (var package in _loadedPackages)
                    {
                        //package.exportMeshNamesToCSV();
                        string dirFullName = package.File.Directory.FullName + "\\..\\ExportAssets\\";

                        var subPack = package.extractSubPackage(missedMatGuids, missedTexGuids);
                        if (subPack.Items.Count > 0)
                            subPack.Save(dirFullName + subPack.File.Name);
                    }
                }
            }

            //string filterCSV = Path.Combine(WorkDir.FullName + "\\filter.csv");
            //var reader = new StreamReader(File.OpenRead(filterCSV));
            //List<string> filterTextList = new List<string>();
            //while (!reader.EndOfStream)
            //{
            //    var line = reader.ReadLine();
            //    //var values = line.Split(';');
            //    if (line != "")
            //        filterTextList.Add(line); 
            //}

            //foreach (var package in _loadedPackages)
            //{
            //    //package.exportMeshNamesToCSV();
            //    string dirFullName = package.File.Directory.FullName + "\\..\\ExportAssets\\";

            //    var subPack = package.extractSubPackage(filterTextList);
            //    if (subPack.Items.Count > 0)
            //        subPack.Save(dirFullName + subPack.File.Name);
            //}
        }

        public AssetItem GetAsset(Guid guid)
        {
            _assetLookup.TryGetValue(guid, out var result);
            return result;
        }

        public T GetAsset<T>(Guid guid) where T : AssetItem
        {
            _assetLookup.TryGetValue(guid, out var result);
            var result2 = result as T;
            return result2;
        }

        bool IDependenceResolver.Resolve<T>(Guid guid, string name, out T result)
        {
            if (guid == Guid.Empty)
            {
                result = null;
                return false;
            }

            if (_assetLookup.TryGetValue(guid, out var res))
            {
                result = res as T;
                return result != null;
            }

            result = null;
            return false;
        }

        public void SetAsDefaultGlobalResolver()
        {
            DefaultDependenceResolver.Instance = this;
        }

        /*public AbstractExternalLoader GetData(Guid guid)
		{
			FixedExternalData.TryGetValue(guid, out var result);
			return result;
		}

		public T GetData<T>(Guid guid) where T : AbstractExternalLoader
		{
			FixedExternalData.TryGetValue(guid, out var result);
			var result2 = result as T;
			return result2;
		}*/

        public sealed class VolatileWork
        {
            public AssetPackage Package { set; get; }

            public List<AssetItem> Items { private set; get; }

            public VolatileWork()
            {
                Items = new List<AssetItem>();
            }
        }
    }
}