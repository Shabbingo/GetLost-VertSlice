using GetLost.Missions;
using UnityEditor;
using UnityEngine;

public static class GetLostMissionPoiMenu
{
    [MenuItem("GameObject/Get Lost/Missions/Major POI Detection Zone", false, 20)]
    private static void CreateMajor() => CreatePoi("Major POI", MissionPoiKind.Major, new Vector3(35f, 12f, 35f));

    [MenuItem("GameObject/Get Lost/Missions/Minor POI Detection Zone", false, 21)]
    private static void CreateMinor() => CreatePoi("Minor POI", MissionPoiKind.Minor, new Vector3(20f, 8f, 20f));

    [MenuItem("GameObject/Get Lost/Missions/Mission Board Manager", false, 22)]
    private static void CreateBoard()
    {
        GameObject board = new("Mission Board Manager");
        Undo.RegisterCreatedObjectUndo(board, "Create Mission Board Manager");
        board.AddComponent<WagonPathMissionDetectionSource>();
        board.AddComponent<MajorPoiSurveyController>();
        board.AddComponent<MissionBoardController>();
        PlaceAtScenePivot(board);
        Selection.activeGameObject = board;
    }

    [MenuItem("GameObject/Get Lost/Missions/Add Display To Selected Mission Board", false, 23)]
    private static void AddDisplayToSelectedBoard()
    {
        GameObject selected = Selection.activeGameObject;
        if (!selected)
        {
            EditorUtility.DisplayDialog("Select Mission Board", "Select the physical Mission Board object in the Hierarchy first.", "OK");
            return;
        }
        MissionBoardWorldDisplay display = selected.GetComponent<MissionBoardWorldDisplay>();
        if (!display)
            display = Undo.AddComponent<MissionBoardWorldDisplay>(selected);
        display.BuildOrRefreshDisplay();
        EditorUtility.SetDirty(selected);
        Selection.activeGameObject = selected;
    }

    private static void CreatePoi(string name, MissionPoiKind kind, Vector3 size)
    {
        GameObject poi = new(name);
        Undo.RegisterCreatedObjectUndo(poi, $"Create {name}");
        BoxCollider area = poi.AddComponent<BoxCollider>();
        area.isTrigger = true;
        area.size = size;
        MissionPointOfInterest component = poi.AddComponent<MissionPointOfInterest>();
        component.ConfigureKind(kind);
        PlaceAtScenePivot(poi);
        Selection.activeGameObject = poi;
    }

    private static void PlaceAtScenePivot(GameObject target)
    {
        if (Selection.activeTransform)
            target.transform.SetParent(Selection.activeTransform, false);
        else if (SceneView.lastActiveSceneView)
            target.transform.position = SceneView.lastActiveSceneView.pivot;
    }
}
