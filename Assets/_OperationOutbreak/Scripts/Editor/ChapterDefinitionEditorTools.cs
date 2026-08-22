#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace OperationOutbreak.EditorTools
{
    /// <summary>
    /// Milestone 1X - editor validation and loading support for ChapterDefinition assets.
    ///
    ///   Tools > Operation Outbreak > Validate Chapter Definitions
    ///
    /// Validation is data-only and read-only: it runs ChapterDefinition.CollectProblems on
    /// every chapter asset (which in turn validates every mission the chapter references),
    /// reporting any identity, sequencing, environment, objective or reward problem. Broken
    /// chapter data fails loudly here so it can never ship an unplayable chapter.
    ///
    /// This mirrors the existing MissionDefinitionEditorTools pattern (validate-all + a
    /// reusable LoadAll for tests) so chapter and mission authoring share one QA workflow.
    /// </summary>
    public static class ChapterDefinitionEditorTools
    {
        /// <summary>Where committed chapter definition assets live.</summary>
        public const string ChapterDefinitionsFolder =
            "Assets/_OperationOutbreak/Resources/ChapterDefinitions";

        [MenuItem("Tools/Operation Outbreak/Validate Chapter Definitions")]
        public static void ValidateAllChapterDefinitions()
        {
            bool valid = ValidateAll(out List<string> problems);

            if (problems.Count == 0)
            {
                Debug.Log("[1X] Chapter definition validation PASSED: every committed chapter " +
                          "has valid identity, sequencing, environment, objective and reward data.");
                EditorUtility.DisplayDialog(
                    "Chapter Definitions",
                    "Chapter definition validation PASSED.",
                    "OK");
                return;
            }

            for (int i = 0; i < problems.Count; i++)
            {
                Debug.LogError("[1X] Chapter definition validation FAILED: " + problems[i]);
            }

            EditorUtility.DisplayDialog(
                "Chapter Definitions",
                "Chapter definition validation FAILED (" + problems.Count + " problem(s)).\n" +
                "See the Console for the chapter, mission and correction.",
                "OK");
        }

        /// <summary>
        /// Validates every ChapterDefinition asset in the project. Returns true when there is
        /// nothing to fix; <paramref name="problems"/> carries one actionable string per problem.
        /// </summary>
        public static bool ValidateAll(out List<string> problems)
        {
            problems = new List<string>();

            HashSet<string> knownArchetypeIds = MissionDefinitionEditorTools.LoadKnownArchetypeIds();
            List<global::OperationOutbreak.Mission.ChapterDefinition> chapters = LoadAllChapterDefinitions();

            for (int i = 0; i < chapters.Count; i++)
            {
                List<string> chapterProblems =
                    global::OperationOutbreak.Mission.ChapterDefinition.CollectProblems(
                        chapters[i], knownArchetypeIds);

                for (int j = 0; j < chapterProblems.Count; j++)
                {
                    problems.Add(chapters[i].name + ": " + chapterProblems[j]);
                }
            }

            if (chapters.Count == 0)
            {
                problems.Add("No ChapterDefinition assets found under " + ChapterDefinitionsFolder +
                             ". Create one via Assets > Create > Operation Outbreak > Chapter Definition.");
            }

            return problems.Count == 0;
        }

        /// <summary>Loads every ChapterDefinition asset in the project (editor).</summary>
        public static List<global::OperationOutbreak.Mission.ChapterDefinition> LoadAllChapterDefinitions()
        {
            List<global::OperationOutbreak.Mission.ChapterDefinition> chapters =
                new List<global::OperationOutbreak.Mission.ChapterDefinition>();

            string[] guids = AssetDatabase.FindAssets("t:ChapterDefinition");

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                global::OperationOutbreak.Mission.ChapterDefinition chapter =
                    AssetDatabase.LoadAssetAtPath<global::OperationOutbreak.Mission.ChapterDefinition>(path);

                if (chapter != null)
                {
                    chapters.Add(chapter);
                }
            }

            return chapters;
        }
    }
}
#endif
