using Collada141;
using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.IO;
using TpacTool.Lib;

namespace TpacTool.IO
{
    public static class ModelExporter
    {
        public static void ExportToFile([NotNull] string path, [CanBeNull] Metamesh model,
            [CanBeNull] Skeleton skeleton = null, ModelExportOption option = 0)
        {
            ExportToFile(path, model, skeleton, null, null, option);
        }

        public static void ExportToFile([NotNull] string path, [CanBeNull] Metamesh model,
            [CanBeNull] Skeleton skeleton = null,
            [CanBeNull] SkeletalAnimation animation = null, [CanBeNull] MorphAnimation morph = null,
            ModelExportOption option = 0)
        {
            if (path.EndsWith(".obj", StringComparison.OrdinalIgnoreCase))
                ExportToFile<WavefrontExporter>(path, model, skeleton, animation, morph, option);
            else if (path.EndsWith(".dae", StringComparison.OrdinalIgnoreCase))
                ExportToFile<ColladaExporter>(path, model, skeleton, animation, morph, option);
            else
                throw new FormatException("Unsupported export format");
        }

        public static void ExportToFile<T>([NotNull] string path, [CanBeNull] Metamesh model,
            [CanBeNull] Skeleton skeleton = null, ModelExportOption option = 0)
            where T : AbstractModelExporter, new()
        {
            ExportToFile<T>(path, model, skeleton, null, null, option);
        }

        public static void ExportToFile<T>([NotNull] string path, [CanBeNull] Metamesh model,
            [CanBeNull] Skeleton skeleton = null,
            [CanBeNull] SkeletalAnimation animation = null, [CanBeNull] MorphAnimation morph = null,
            ModelExportOption option = 0)
            where T : AbstractModelExporter, new()
        {
            T exporter = new T();
            ExportToFile(exporter, path, model, skeleton, animation, morph, option);
        }

        public static void ExportToFile([NotNull] AbstractModelExporter exporter, [NotNull] string path, [CanBeNull] Metamesh model,
            [CanBeNull] Skeleton skeleton = null,
            [CanBeNull] SkeletalAnimation animation = null, [CanBeNull] MorphAnimation morph = null,
            ModelExportOption option = 0)
        {
            if (model == null)
                model = Metamesh.EmptyMesh;

            var dirPath = Path.GetDirectoryName(path) + "/";

            var exportedMeshes = model.Meshes;
            var lodMask = int.MaxValue;
            if (!option.HasFlag(ModelExportOption.ExportAllLod))
            {
                lodMask = 1;
                exportedMeshes = exportedMeshes.FindAll(mesh => mesh.Lod == 0);
            }

            string parmStr = "";
            HashSet<Texture> textures = new HashSet<Texture>();
            foreach (var mesh in exportedMeshes)
            {
                if (mesh.Lod == 0)
                {
                    parmStr += "Mesh: " + mesh.Name;
                    if (mesh.Flags.Count > 0)
                    {
                        parmStr += "\r\n\tFlags: ";
                        foreach (var flag in mesh.Flags)
                        {
                            parmStr += "\t" + flag;
                        }
                    }
                    if (mesh.MaterialFlags.Count > 0)
                    {
                        parmStr += "\r\n\tMaterialFlags: ";
                        foreach (var flag in mesh.MaterialFlags)
                        {
                            parmStr += "\t" + flag;
                        }
                    }
                    if (mesh.ClothingMaterial != null && mesh.ClothingMaterial.Name.Length > 0)
                    {
                        parmStr += "\r\n\tClothingMaterial: " + mesh.ClothingMaterial.Name;
                        parmStr += "\r\n\t\tBendingStiffness: " + mesh.ClothingMaterial.BendingStiffness.ToString();
                        parmStr += "\r\n\t\tShearingStiffness: " + mesh.ClothingMaterial.ShearingStiffness.ToString();
                        parmStr += "\r\n\t\tStretchingStiffness: " + mesh.ClothingMaterial.StretchingStiffness.ToString();
                        parmStr += "\r\n\t\tAnchorStiffness: " + mesh.ClothingMaterial.AnchorStiffness.ToString();
                        parmStr += "\r\n\t\tDamping: " + mesh.ClothingMaterial.Damping.ToString();
                        parmStr += "\r\n\t\tLinearInertia: " + mesh.ClothingMaterial.LinearInertia.ToString();
                        parmStr += "\r\n\t\tAirDragMultiplier: " + mesh.ClothingMaterial.AirDragMultiplier.ToString();
                        parmStr += "\r\n\t\tWind: " + mesh.ClothingMaterial.Wind.ToString();
                        parmStr += "\r\n\t\tGravity: " + mesh.ClothingMaterial.Gravity.ToString();
                        parmStr += "\r\n\t\tMaxLinearVelocity: " + mesh.ClothingMaterial.MaxLinearVelocity.ToString();
                        parmStr += "\r\n\t\tLinearVelocityMultiplier: " + mesh.ClothingMaterial.LinearVelocityMultiplier.ToString();
                    }
                    parmStr += "\r\n";
                }

                TpacTool.Lib.Material validMaterial = null;
                if (mesh.Material.TryGetItem(out var mat1))
                {
                    foreach (var texDep in mat1.Textures.Values)
                    {
                        if (texDep.TryGetItem(out var tex))
                            textures.Add(tex);
                    }
                    validMaterial = mat1;
                }
                else if (exporter.SupportsSecondMaterial && mesh.SecondMaterial.TryGetItem(out var mat2))
                {
                    foreach (var texDep in mat2.Textures.Values)
                    {
                        if (texDep.TryGetItem(out var tex))
                            textures.Add(tex);
                    }
                    validMaterial = mat2;
                }

                if (validMaterial != null && mesh.Lod == 0)
                {
                    parmStr += "\r\n";
                    parmStr += "Material: " + validMaterial.Name;
                    if (validMaterial.Flags.Count > 0)
                    {
                        parmStr += "\r\n\t Flags: ";
                        foreach (var flag in validMaterial.Flags)
                        {
                            parmStr += flag;
                            parmStr += "\t";
                        }
                    }
                    if (validMaterial.ShaderMaterialFlags.Count > 0)
                    {
                        parmStr += "\r\n\t ShaderMaterialFlags: ";
                        foreach (var flag in validMaterial.ShaderMaterialFlags)
                        {
                            parmStr += flag;
                            parmStr += "\t";
                        }
                    }
                    if (validMaterial.VertexLayoutFlags.Count > 0)
                    {
                        parmStr += "\r\n\t VertexLayoutFlags: ";
                        foreach (var flag in validMaterial.VertexLayoutFlags)
                        {
                            parmStr += flag;
                            parmStr += "\t";
                        }
                    }
                    if (validMaterial.Textures.Count > 0)
                    {
                        parmStr += "\r\nTextures: \r\n\t";
                        foreach (var texDep in validMaterial.Textures.Values)
                        {
                            if (texDep.TryGetItem(out var tex) && tex.HasPixelData)
                            {
                                parmStr += tex.Name;
                                parmStr += "\r\n\t";
                            }
                        }
                    }
                    parmStr += "\r\n************************************";
                }
            }

            string prefix = option.HasFlag(ModelExportOption.ExportTexturesSubFolder)
                ? model.Name + "/"
                : string.Empty;
            foreach (var tex in textures)
            {
                var texRelPath = prefix + tex.Name + "." + GetTextureFormat(tex.Format, option);
                exporter.TexturePathMapping[tex] = texRelPath;
                var texFullPath = dirPath + texRelPath;
                if (tex.HasPixelData && (option.HasFlag(ModelExportOption.ExportTextures) ||
                                        option.HasFlag(ModelExportOption.ExportTexturesSubFolder)))
                    TextureExporter.ExportToFile(texFullPath, tex);
            }

            exporter.Model = model;
            exporter.Skeleton = skeleton;
            exporter.Animation = animation;
            exporter.Morph = morph;
            exporter.LodMask = lodMask;
            exporter.FixBoneForBlender = option.HasFlag(ModelExportOption.FixBoneForBlender);
            exporter.IsNegYAxisForward = option.HasFlag(ModelExportOption.NegYAxisForward);
            exporter.IsYAxisUp = option.HasFlag(ModelExportOption.YAxisUp);
            exporter.IsLargerSize = option.HasFlag(ModelExportOption.LargerSize);
            exporter.IsDiffuseOnly = option.HasFlag(ModelExportOption.ExportDiffuseOnly);
            exporter.Export(path);

            if (option.HasFlag(ModelExportOption.ExportParams))
            {
                string configFile = Path.Combine(path.Replace("_ori.fbx", ".log"));
                System.IO.File.WriteAllText(configFile, parmStr);
            }
        }

        private static string GetTextureFormat(TextureFormat format, ModelExportOption option)
        {
            return MaterialExporter.GetBestTextureFormat(format, (MaterialExporter.MaterialExportOption)option);
        }

        /*public static bool CheckAssimpInited()
		{
			return AssimpLibrary.Instance.IsLibraryLoaded;
		}*/

        [Flags]
        public enum ModelExportOption
        {
            LargerSize = 0x1,
            YAxisUp = 0x2,
            NegYAxisForward = 0x4,
            ExportTextures = 0x1000,
            ExportTexturesSubFolder = 0x2000,
            ExportDiffuseOnly = 0x4000,
            FixBoneForBlender = 0x10000,

            //PreferPng = 0x20000,
            //PreferDds = 0x40000,
            ExportAllLod = 0x20000,

            ExportParams = 0x40000
        }
    }
}