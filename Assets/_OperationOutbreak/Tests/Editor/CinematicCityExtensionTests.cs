using NUnit.Framework;
using OperationOutbreak.Cinematic;
using UnityEngine;

namespace OperationOutbreak.Tests
{
    /// <summary>
    /// Milestone 1Z.1A — tests for the cinematic city extension. They build the extension under a
    /// temporary parent with null materials (structure-only) and assert it is a clean, visual-only,
    /// gameplay-safe skirt around the Chapter 1 playable corridor. Preserves the existing baseline.
    /// </summary>
    public sealed class CinematicCityExtensionTests
    {
        private GameObject _root;

        [SetUp]
        public void SetUp()
        {
            var holder = new GameObject("ExtensionHolder");
            _root = CinematicCityExtension.Build(holder.transform, new CinematicCityExtension.Materials());
            // holder is the parent; keep it for cleanup via the root's top parent.
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root.transform.parent != null ? _root.transform.parent.gameObject : _root);
        }

        [Test]
        public void ExtensionBuildsHierarchyWithAllGroups()
        {
            Assert.IsNotNull(_root, "Extension root must be created.");
            Assert.AreEqual(CinematicCityExtension.RootName, _root.name);
            foreach (string group in new[]
            {
                CinematicCityExtension.GroupMidground, CinematicCityExtension.GroupFarCity,
                CinematicCityExtension.GroupLandmarks, CinematicCityExtension.GroupSmoke,
                CinematicCityExtension.GroupBoundaryFill, CinematicCityExtension.GroupHaze,
            })
            {
                Assert.IsNotNull(_root.transform.Find(group), "Group '" + group + "' must exist.");
            }
        }

        [Test]
        public void ExtensionHasNoColliders()
        {
            // Visual-only: nothing in the extension may carry a collider (would risk gameplay physics).
            Assert.AreEqual(0, _root.GetComponentsInChildren<Collider>(true).Length,
                "Cinematic extension must not contain any colliders.");
        }

        [Test]
        public void ExtensionHasNoGameplayScripts()
        {
            // The extension is pure geometry; no MonoBehaviour (gameplay or otherwise) may live on it.
            Assert.AreEqual(0, _root.GetComponentsInChildren<MonoBehaviour>(true).Length,
                "Cinematic extension must not carry any gameplay/scripts.");
        }

        [Test]
        public void FarCityAndDistantLayersDoNotCastShadows()
        {
            // Mobile-conscious: far silhouettes, smoke and boundary/haze never cast realtime shadows.
            CheckNoShadows(CinematicCityExtension.GroupFarCity);
            CheckNoShadows(CinematicCityExtension.GroupSmoke);
            CheckNoShadows(CinematicCityExtension.GroupBoundaryFill);
            CheckNoShadows(CinematicCityExtension.GroupHaze);
        }

        [Test]
        public void NoStructurePlacedInsidePlayableCorridor()
        {
            // Every vertical STRUCTURE must sit outside the playable corridor band so gameplay is
            // never obstructed. (Ground skirt / haze legitimately span the area, so are exempt.)
            int violated = 0;
            foreach (string group in CinematicCityExtension.StructureGroups)
            {
                Transform g = _root.transform.Find(group);
                if (g == null) continue;
                foreach (Transform child in g)
                {
                    Vector3 p = child.position;
                    if (CinematicCityExtension.IsInsideCorridor(p.x, p.z)) violated++;
                }
            }
            Assert.AreEqual(0, violated,
                "No extension structure may be placed inside the playable corridor keep-out band.");
        }

        [Test]
        public void ExtensionHasVariedCityContent()
        {
            Assert.GreaterOrEqual(CountRenderers(CinematicCityExtension.GroupMidground), 30,
                "Midground must contain a varied set of ruined structures.");
            Assert.GreaterOrEqual(CountRenderers(CinematicCityExtension.GroupFarCity), 25,
                "FarCity must contain many distant silhouettes.");
            Assert.GreaterOrEqual(CountRenderers(CinematicCityExtension.GroupSmoke), 5,
                "Smoke must contain several columns.");
            Assert.GreaterOrEqual(CountRenderers(CinematicCityExtension.GroupLandmarks), 5,
                "Landmarks must contain several large structures.");
        }

        private void CheckNoShadows(string group)
        {
            Transform g = _root.transform.Find(group);
            if (g == null) return;
            foreach (Renderer r in g.GetComponentsInChildren<Renderer>(true))
            {
                Assert.AreNotEqual(UnityEngine.Rendering.ShadowCastingMode.On, r.shadowCastingMode,
                    group + " object '" + r.name + "' must not cast realtime shadows (mobile).");
            }
        }

        private int CountRenderers(string group)
        {
            Transform g = _root.transform.Find(group);
            return g == null ? 0 : g.GetComponentsInChildren<Renderer>(true).Length;
        }
    }
}
