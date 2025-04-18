﻿using JetBrains.Annotations;
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

        public void extractPackageByFilterText()
        {
            HashSet<string> markedMetaMeshNames = new HashSet<string>();
            string filterCSV = Path.Combine(WorkDir.FullName + "\\bookMarks.csv");
            if (File.Exists(filterCSV))
            {
                var reader = new StreamReader(File.OpenRead(filterCSV));
                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    //var values = line.Split(';');
                    if (line != "")
                        markedMetaMeshNames.Add(line);
                }
            }

            //Keep some PhysicsShapes 
            HashSet<string> markedPhysicsShapesNames = new HashSet<string>();
            filterCSV = Path.Combine(WorkDir.FullName + "\\PhysicsShapeFilter.csv");
            if (File.Exists(filterCSV))
            {
                var reader = new StreamReader(File.OpenRead(filterCSV));
                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    if (line != "")
                        markedPhysicsShapesNames.Add(line);
                }
            }

            if (markedMetaMeshNames.Count > 0)
            {
                HashSet<Guid> metaMeshGuids = new HashSet<Guid>();
                HashSet<Guid> depMatGuids = new HashSet<Guid>();
                HashSet<Guid> depTexGuids = new HashSet<Guid>();
                HashSet<Guid> PhysicsShapeGuids = new HashSet<Guid>();
                foreach (var item in _loadedAssets)
                {
                    if (item.Type == Metamesh.TYPE_GUID && item is Metamesh metamesh)
                    {
                        foreach (var mesh in metamesh.Meshes)
                        {
                            if (mesh.Lod > 0)
                                continue;

                            string meshName = metamesh.Name;
                            meshName = Utils.RemoveSuffix(meshName, "_converted");
                            meshName = Utils.RemoveSuffix(meshName, "_converted_slim");

                            bool bHit = false;
                            foreach (var filterText in markedMetaMeshNames)
                            {
                                if (filterText.EndsWith("*"))
                                {
                                    var prefix = Utils.RemoveSuffix(filterText, "*");
                                    if (meshName.StartsWith(prefix))
                                    {
                                        bHit = true;
                                        break;
                                    }
                                }

                                if (meshName.Equals(filterText))
                                {
                                    bHit = true;
                                    break;
                                }
                            }

                            if (bHit)
                            {
                                metaMeshGuids.Add(item.Guid);
                                if (mesh.Material.Guid != Guid.Empty)
                                {
                                    depMatGuids.Add(mesh.Material.Guid);
                                    if (mesh.Material.TryGetItem(out var mat1))
                                    {
                                        var mat = mat1 as Material;
                                        foreach (var texDep in mat.Textures.Values)
                                        {
                                            if (texDep.TryGetItem(out var tex))
                                                depTexGuids.Add(tex.Guid);
                                        }
                                    }
                                }
                                else if (mesh.SecondMaterial.Guid != Guid.Empty)
                                {
                                    depMatGuids.Add(mesh.SecondMaterial.Guid);
                                    if (mesh.SecondMaterial.TryGetItem(out var mat2))
                                    {
                                        var mat = mat2 as Material;
                                        foreach (var texDep in mat.Textures.Values)
                                        {
                                            if (texDep.TryGetItem(out var tex))
                                                depTexGuids.Add(tex.Guid);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else if (item.Type == PhysicsShape.TYPE_GUID)
                    {
                        bool bHit = false;
                        foreach (var filterText in markedPhysicsShapesNames)
                        {
                            if (item.Name.StartsWith(filterText))
                            {
                                bHit = true;
                                break;
                            }
                        }

                        if (bHit)
                        {
                            PhysicsShapeGuids.Add(item.Guid);
                        }
                    }
                }

                if (metaMeshGuids.Count > 0)
                {
                    string dirFullName = WorkDir.FullName + "\\..\\ExportAssets\\";
                    DirectoryInfo directory = new DirectoryInfo(dirFullName);
                    if (!directory.Exists)
                        directory.Create();

                    foreach (FileInfo file in directory.GetFiles())
                    {
                        file.Delete();
                    }

                    AssetPackage mergePack = null;
                    foreach (var package in _loadedPackages)
                    {
                        var subPack = package.ExtractSubPackage(metaMeshGuids, depMatGuids, depTexGuids, PhysicsShapeGuids);
                        if (subPack.Items.Count > 0)
                        {
                            if (mergePack == null)
                                mergePack = subPack;
                            else
                                mergePack.mergePackage(subPack);
                        }
                    }

                    if (mergePack != null && mergePack.Items.Count > 0)
                    {
                        string fullName = dirFullName + mergePack.File.Name;
                        mergePack.Save(fullName);
                        mergePack.ExportMeshNamesToCSV(fullName.Replace(".tpac", ".csv"));
                    }
                }
            }
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