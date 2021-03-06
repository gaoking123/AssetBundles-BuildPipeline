﻿using System;
using System.Diagnostics;
using UnityEditor.Build.AssetBundle.Shared;
using UnityEditor.Build.Utilities;
using UnityEditor.Experimental.Build.AssetBundle;
using UnityEditor.Experimental.Build.Player;

namespace UnityEditor.Build.AssetBundle
{
    public static class BundleBuildPipeline
    {
        public const string kTempBundleBuildPath = "Temp/BundleBuildData";

        public const string kDefaultOutputPath = "AssetBundles";

        // TODO: Replace with calls to UnityEditor.Build.BuildPipelineInterfaces once i make it more generic & public
        public static Func<BuildDependencyInformation, object, BuildPipelineCodes> PostBuildDependency;
        // TODO: Callback PostBuildPacking can't modify BuildCommandSet due to pass by value...will change to class
        public static Func<BuildCommandSet, object, BuildPipelineCodes> PostBuildPacking;

        public static Func<BundleBuildResult, object, BuildPipelineCodes> PostBuildWriting;

        public static BuildSettings GenerateBundleBuildSettings(TypeDB typeDB)
        {
            var settings = new BuildSettings();
            settings.target = EditorUserBuildSettings.activeBuildTarget;
            settings.group = BuildPipeline.GetBuildTargetGroup(settings.target);
            settings.typeDB = typeDB;
            return settings;
        }

        public static BuildSettings GenerateBundleBuildSettings(TypeDB typeDB, BuildTarget target)
        {
            var settings = new BuildSettings();
            settings.target = target;
            settings.group = BuildPipeline.GetBuildTargetGroup(settings.target);
            settings.typeDB = typeDB;
            return settings;
        }

        public static BuildSettings GenerateBundleBuildSettings(TypeDB typeDB, BuildTarget target, BuildTargetGroup group)
        {
            var settings = new BuildSettings();
            settings.target = target;
            settings.group = group;
            settings.typeDB = typeDB;
            // TODO: Validate target & group
            return settings;
        }

        public static BuildPipelineCodes BuildAssetBundles(BuildInput input, BuildSettings settings, BuildCompression compression, string outputFolder, out BundleBuildResult result, object callbackUserData = null, bool useCache = true)
        {
            var buildTimer = new Stopwatch();
            buildTimer.Start();

            if (ProjectValidator.HasDirtyScenes())
            {
                result = new BundleBuildResult();
                buildTimer.Stop();
                BuildLogger.LogError("Build Asset Bundles failed in: {0:c}. Error: {1}.", buildTimer.Elapsed, BuildPipelineCodes.UnsavedChanges);
                return BuildPipelineCodes.UnsavedChanges;
            }
            
            var exitCode = BuildPipelineCodes.Success;
            result = new BundleBuildResult();

            AssetDatabase.SaveAssets();
                
            // TODO: Until new AssetDatabaseV2 is online, we need to switch platforms
            EditorUserBuildSettings.SwitchActiveBuildTarget(settings.group, settings.target);

            var stepCount = BundleDependencyStep.StepCount + BundlePackingStep.StepCount + BundleWritingStep.StepCount;
            using (var progressTracker = new BuildProgressTracker(stepCount))
            {
                using (var buildCleanup = new BuildStateCleanup(true, kTempBundleBuildPath))
                {
                    BuildDependencyInformation buildInfo;
                    exitCode = BundleDependencyStep.Build(input, settings, out buildInfo, useCache, progressTracker);
                    if (exitCode < BuildPipelineCodes.Success)
                        return exitCode;

                    if (PostBuildDependency != null)
                    {
                        exitCode = PostBuildDependency.Invoke(buildInfo, callbackUserData);
                        if (exitCode < BuildPipelineCodes.Success)
                            return exitCode;
                    }

                    BuildCommandSet commandSet;
                    exitCode = BundlePackingStep.Build(buildInfo, out commandSet, useCache, progressTracker);
                    if (exitCode < BuildPipelineCodes.Success)
                        return exitCode;

                    if (PostBuildPacking != null)
                    {
                        // TODO: Callback PostBuildPacking can't modify BuildCommandSet due to pass by value...will change to class
                        exitCode = PostBuildPacking.Invoke(commandSet, callbackUserData);
                        if (exitCode < BuildPipelineCodes.Success)
                            return exitCode;
                    }

                    exitCode = BundleWritingStep.Build(settings, compression, outputFolder, buildInfo, commandSet, out result, useCache, progressTracker);
                    if (exitCode < BuildPipelineCodes.Success)
                        return exitCode;

                    if (PostBuildWriting != null)
                    {
                        exitCode = PostBuildWriting.Invoke(result, callbackUserData);
                        if (exitCode < BuildPipelineCodes.Success)
                            return exitCode;
                    }
                }
            }

            buildTimer.Stop();
            if (exitCode >= BuildPipelineCodes.Success)
                BuildLogger.Log("Build Asset Bundles successful in: {0:c}", buildTimer.Elapsed);
            else if (exitCode == BuildPipelineCodes.Canceled)
                BuildLogger.LogWarning("Build Asset Bundles canceled in: {0:c}", buildTimer.Elapsed);
            else
                BuildLogger.LogError("Build Asset Bundles failed in: {0:c}. Error: {1}.", buildTimer.Elapsed, exitCode);

            return exitCode;
        }
    }
}