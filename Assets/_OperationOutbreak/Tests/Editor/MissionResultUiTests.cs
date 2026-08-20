using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using OperationOutbreak.Rewards;
using OperationOutbreak.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OperationOutbreak.Tests
{
    /// <summary>
    /// Milestone 1V QA fix #2 - regression tests for the Result UI button-action
    /// mapping. The real defect: the oversized button LABELS were raycast targets, so
    /// the "RETURN" label (rendered on top, overlapping the RETRY button) captured
    /// clicks meant for RETRY and bubbled them to the RETURN button. These tests build
    /// the actual controllers (runtime-built UI), click the real Button.onClick and
    /// assert the correct navigation intent fires - and that the labels no longer
    /// intercept and the button rects stay non-overlapping.
    /// </summary>
    public sealed class MissionResultUiTests
    {
        private sealed class Counters
        {
            public int Retry;
            public int Return;
            public int Next;
        }

        // ------------------------------------------------------------------ helpers

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Field '" + fieldName + "' missing on " + target.GetType().Name + ".");
            field.SetValue(target, value);
        }

        private static MissionResultNavigation NewNavigation(Counters counters)
        {
            GameObject host = new GameObject("NavigationHost");
            host.SetActive(false);
            MissionResultNavigation navigation = host.AddComponent<MissionResultNavigation>();
            host.SetActive(true);
            navigation.RetryRequested += () => counters.Retry++;
            navigation.ReturnRequested += () => counters.Return++;
            navigation.NextRequested += () => counters.Next++;
            return navigation;
        }

        private static MissionCompleteController BuildCompleteController(MissionResultNavigation navigation)
        {
            GameObject host = new GameObject("CompleteHost");
            host.SetActive(false);
            MissionCompleteController controller = host.AddComponent<MissionCompleteController>();
            if (navigation != null)
            {
                SetField(controller, "resultNavigation", navigation);
            }

            host.SetActive(true); // Awake -> Build(); OnEnable -> subscriptions.
            return controller;
        }

        private static GameOverController BuildGameOverController(MissionResultNavigation navigation)
        {
            GameObject host = new GameObject("GameOverHost");
            host.SetActive(false);
            GameOverController controller = host.AddComponent<GameOverController>();
            if (navigation != null)
            {
                SetField(controller, "resultNavigation", navigation);
            }

            host.SetActive(true);
            return controller;
        }

        private static Button FindButton(Component controller, string buttonName)
        {
            Transform found = FindRecursive(controller.transform, buttonName);
            Assert.IsNotNull(found, buttonName + " must be created under " + controller.GetType().Name + ".");
            Button button = found.GetComponent<Button>();
            Assert.IsNotNull(button, buttonName + " must carry a Button component.");
            return button;
        }

        private static Transform FindRecursive(Transform root, string name)
        {
            if (root.name == name)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindRecursive(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        // ------------------------------------------------- Mission Complete mapping

        [Test]
        public void MissionCompleteRetryButtonMapsToRequestRetryNotReturn()
        {
            Counters counters = new Counters();
            MissionResultNavigation navigation = NewNavigation(counters);
            MissionCompleteController controller = BuildCompleteController(navigation);

            try
            {
                Button retry = FindButton(controller, "RetryButton");
                retry.onClick.Invoke();

                Assert.AreEqual(1, counters.Retry,
                    "Clicking RETRY must invoke RequestRetry exactly once.");
                Assert.AreEqual(0, counters.Return,
                    "Clicking RETRY must NOT invoke RequestReturn.");
                Assert.AreEqual(0, counters.Next,
                    "Clicking RETRY must NOT invoke RequestNext.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(controller.gameObject);
                UnityEngine.Object.DestroyImmediate(navigation.gameObject);
            }
        }

        [Test]
        public void MissionCompleteReturnButtonMapsToRequestReturnNotRetry()
        {
            Counters counters = new Counters();
            MissionResultNavigation navigation = NewNavigation(counters);
            MissionCompleteController controller = BuildCompleteController(navigation);

            try
            {
                Button returnButton = FindButton(controller, "ReturnButton");
                returnButton.onClick.Invoke();

                Assert.AreEqual(1, counters.Return,
                    "Clicking RETURN must invoke RequestReturn exactly once.");
                Assert.AreEqual(0, counters.Retry,
                    "Clicking RETURN must NOT invoke RequestRetry.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(controller.gameObject);
                UnityEngine.Object.DestroyImmediate(navigation.gameObject);
            }
        }

        // ------------------------------------------------- Game Over mapping

        [Test]
        public void GameOverRetryButtonMapsToRequestRetry()
        {
            Counters counters = new Counters();
            MissionResultNavigation navigation = NewNavigation(counters);
            GameOverController controller = BuildGameOverController(navigation);

            try
            {
                Button retry = FindButton(controller, "RetryButton");
                retry.onClick.Invoke();

                Assert.AreEqual(1, counters.Retry,
                    "Game Over RETRY must invoke RequestRetry.");
                Assert.AreEqual(0, counters.Return,
                    "Game Over RETRY must NOT invoke RequestReturn.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(controller.gameObject);
                UnityEngine.Object.DestroyImmediate(navigation.gameObject);
            }
        }

        [Test]
        public void GameOverReturnButtonMapsToRequestReturn()
        {
            Counters counters = new Counters();
            MissionResultNavigation navigation = NewNavigation(counters);
            GameOverController controller = BuildGameOverController(navigation);

            try
            {
                Button returnButton = FindButton(controller, "ReturnButton");
                returnButton.onClick.Invoke();

                Assert.AreEqual(1, counters.Return,
                    "Game Over RETURN must invoke RequestReturn.");
                Assert.AreEqual(0, counters.Retry,
                    "Game Over RETURN must NOT invoke RequestRetry.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(controller.gameObject);
                UnityEngine.Object.DestroyImmediate(navigation.gameObject);
            }
        }

        // ------------------------------------------------- hit-region / raycast root cause

        [Test]
        public void ResultLabelsDoNotInterceptClicksAndButtonsDoNotOverlap()
        {
            // Pins the actual root cause: the labels must not be raycast targets, and
            // the two buttons must have independent, non-overlapping clickable regions.
            Counters counters = new Counters();
            MissionResultNavigation navigation = NewNavigation(counters);
            MissionCompleteController complete = BuildCompleteController(navigation);
            GameOverController gameOver = BuildGameOverController(navigation);

            try
            {
                AssertNonOverlappingIndependentButtons(complete, "RetryButton", "ReturnButton");
                AssertNonOverlappingIndependentButtons(gameOver, "RetryButton", "ReturnButton");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(complete.gameObject);
                UnityEngine.Object.DestroyImmediate(gameOver.gameObject);
                UnityEngine.Object.DestroyImmediate(navigation.gameObject);
            }
        }

        private static void AssertNonOverlappingIndependentButtons(
            Component controller, string leftName, string rightName)
        {
            Button left = FindButton(controller, leftName);
            Button right = FindButton(controller, rightName);

            // The label must never intercept: only the button's own Image may be a target.
            TextMeshProUGUI leftLabel = left.GetComponentInChildren<TextMeshProUGUI>();
            TextMeshProUGUI rightLabel = right.GetComponentInChildren<TextMeshProUGUI>();
            Assert.IsNotNull(leftLabel, leftName + " must carry its label.");
            Assert.IsNotNull(rightLabel, rightName + " must carry its label.");
            Assert.IsFalse(leftLabel.raycastTarget,
                leftName + "'s label must NOT be a raycast target (it would intercept clicks).");
            Assert.IsFalse(rightLabel.raycastTarget,
                rightName + "'s label must NOT be a raycast target (it would intercept clicks).");
            Assert.IsTrue(left.image.raycastTarget, leftName + "'s own Image must stay clickable.");
            Assert.IsTrue(right.image.raycastTarget, rightName + "'s own Image must stay clickable.");

            // Independent, non-overlapping clickable rects in the shared parent space.
            RectTransform leftRect = (RectTransform)left.transform;
            RectTransform rightRect = (RectTransform)right.transform;
            Assert.AreSame(leftRect.parent, rightRect.parent,
                "Both buttons must share the same panel parent.");

            float leftRightEdge = leftRect.anchoredPosition.x + leftRect.rect.width * 0.5f;
            float rightLeftEdge = rightRect.anchoredPosition.x - rightRect.rect.width * 0.5f;

            Assert.LessOrEqual(leftRightEdge, rightLeftEdge + 0.001f,
                "The two buttons must not overlap: clicking one must never reach the other.");
        }

        // ------------------------------------------------- single intent per click

        [Test]
        public void SingleClickFiresExactlyOneNavigationIntent()
        {
            Counters counters = new Counters();
            MissionResultNavigation navigation = NewNavigation(counters);
            MissionCompleteController controller = BuildCompleteController(navigation);

            try
            {
                Button retry = FindButton(controller, "RetryButton");
                retry.onClick.Invoke();
                retry.onClick.Invoke();
                retry.onClick.Invoke();

                Assert.AreEqual(3, counters.Retry,
                    "Each click on RETRY must fire exactly one RetryRequested (no duplicates per click).");
                Assert.AreEqual(0, counters.Return,
                    "RETRY must never fire ReturnRequested, however many times it is clicked.");

                Button returnButton = FindButton(controller, "ReturnButton");
                returnButton.onClick.Invoke();

                Assert.AreEqual(1, counters.Return,
                    "Clicking RETURN must fire exactly one ReturnRequested.");
                Assert.AreEqual(3, counters.Retry,
                    "RETURN must not affect the RetryRequested count.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(controller.gameObject);
                UnityEngine.Object.DestroyImmediate(navigation.gameObject);
            }
        }
    }
}
