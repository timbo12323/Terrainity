using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Terrainity.Editor
{
    public sealed partial class TerrainityWindow
    {
        [SerializeField] Vector3 previewOrbit = new Vector3(35, 10, 1);
        [SerializeField] Vector3 previewPan;
        [SerializeField] Vector3 lodPreviewOrbit = new Vector3(35, 10, 2);
        [SerializeField] Vector3 lodPreviewPan;
        bool undoInputInstalled, undoDragging, restoringUndo;
        int undoDragGroup = -1;

        void InstallUndoInput()
        {
            if (undoInputInstalled) return;
            undoInputInstalled = true;
            var root = rootVisualElement;
            root.RegisterCallback<MouseDownEvent>(e =>
            {
                if (e.button != 0 && e.button != 2) return;
                EndUndoDrag();
                Undo.IncrementCurrentGroup();
                undoDragGroup = Undo.GetCurrentGroup();
                undoDragging = true;
                RecordSettingsUndo();
            }, TrickleDown.TrickleDown);
            root.RegisterCallback<MouseMoveEvent>(_ => { if (undoDragging) RecordSettingsUndo(); }, TrickleDown.TrickleDown);
            root.RegisterCallback<MouseUpEvent>(_ =>
            {
                if (!undoDragging) return;
                RecordSettingsUndo();
                // Button and slider handlers finish after this trickle-down callback.
                EditorApplication.delayCall -= EndUndoDrag;
                EditorApplication.delayCall += EndUndoDrag;
            }, TrickleDown.TrickleDown);
            root.RegisterCallback<WheelEvent>(_ => RecordSettingsUndo(), TrickleDown.TrickleDown);
            root.RegisterCallback<KeyDownEvent>(e =>
            {
                if ((e.ctrlKey || e.commandKey) && e.keyCode == KeyCode.Z)
                {
                    EndUndoDrag();
                    Undo.FlushUndoRecordObjects();
                    if (e.shiftKey) Undo.PerformRedo(); else Undo.PerformUndo();
                    e.StopImmediatePropagation();
                    return;
                }
                RecordSettingsUndo();
            }, TrickleDown.TrickleDown);
            // Record before field callbacks mutate nested recipes, gradients or light lists.
            ObserveUndoChange<float>(); ObserveUndoChange<int>(); ObserveUndoChange<bool>();
            ObserveUndoChange<string>(); ObserveUndoChange<Color>(); ObserveUndoChange<Gradient>();
            ObserveUndoChange<Vector2>(); ObserveUndoChange<Vector3>(); ObserveUndoChange<UnityEngine.Object>();
            ObserveUndoChange<Enum>();
        }

        void ObserveUndoChange<T>() => rootVisualElement.RegisterCallback<ChangeEvent<T>>(_ => RecordSettingsUndo(), TrickleDown.TrickleDown);

        internal void RecordSettingsUndo()
        {
            if (this == null || restoringUndo) return;
            if (!undoDragging) Undo.IncrementCurrentGroup();
            Undo.RecordObject(this, "Terrainity settings");
        }

        void EndUndoDrag()
        {
            EditorApplication.delayCall -= EndUndoDrag;
            if (!undoDragging) return;
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(undoDragGroup);
            undoDragging = false; undoDragGroup = -1;
        }

        void OnLostFocus() => EndUndoDrag();

        void RestoreUndoSettings()
        {
            if (this == null || builderPage == null) return;
            restoringUndo = true;
            try
            {
                CancelPreviewUpdate();
                var controls = rootVisualElement.Q<ScrollView>(className: "controls");
                var panel = rootVisualElement.Q<ScrollView>(className: "preview-panel");
                Vector2 controlsOffset = controls?.scrollOffset ?? Vector2.zero;
                Vector2 panelOffset = panel?.scrollOffset ?? Vector2.zero;
                ShowTab(currentTab);
                var newControls = rootVisualElement.Q<ScrollView>(className: "controls");
                var newPanel = rootVisualElement.Q<ScrollView>(className: "preview-panel");
                newControls?.schedule.Execute(() => newControls.scrollOffset = controlsOffset);
                newPanel?.schedule.Execute(() => newPanel.scrollOffset = panelOffset);
                SavePreviewPreferences();
                Repaint();
            }
            finally { restoringUndo = false; }
        }

        void CapturePreviewView()
        {
            if (preview == null) return;
            if (previewMode == 1)
            {
                lodPreviewOrbit = preview.ViewOrbit;
                lodPreviewPan = preview.ViewPan;
            }
            else
            {
                previewOrbit = preview.ViewOrbit;
                previewPan = preview.ViewPan;
            }
        }
    }
}
