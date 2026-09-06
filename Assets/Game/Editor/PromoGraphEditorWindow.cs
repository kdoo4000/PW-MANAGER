using System;
using System.Collections.Generic;
using System.Linq;
using PWManager.Data.Definitions;
using PWManager.Data.Validation;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Editor
{
    public sealed class PromoGraphEditorWindow : EditorWindow
    {
        private PromoTemplateGraphAsset graphAsset;
        private PromoGraphView graphView;
        private Label statusLabel;

        [MenuItem("PW Manager/Promo Template Graph")]
        public static void Open() => GetWindow<PromoGraphEditorWindow>("Promo Graph");

        public static void Open(PromoTemplateGraphAsset asset)
        {
            var window = GetWindow<PromoGraphEditorWindow>("Promo Graph");
            window.SetAsset(asset);
        }

        private void CreateGUI()
        {
            var toolbar = new Toolbar();
            toolbar.Add(new ToolbarButton(CreateGraphAsset) { text = "New Graph" });
            var assetField = new ObjectField("Graph")
            {
                objectType = typeof(PromoTemplateGraphAsset),
                allowSceneObjects = false,
                value = graphAsset
            };
            assetField.RegisterValueChangedCallback(evt => SetAsset(evt.newValue as PromoTemplateGraphAsset));
            toolbar.Add(assetField);

            var addMenu = new ToolbarMenu { text = "Add Node" };
            foreach (PromoGraphNodeType type in Enum.GetValues(typeof(PromoGraphNodeType)))
                addMenu.menu.AppendAction(type.ToString(), _ => AddNode(type), _ => CanAdd(type));
            toolbar.Add(addMenu);
            toolbar.Add(new ToolbarButton(Save) { text = "Save" });
            toolbar.Add(new ToolbarButton(ValidateGraph) { text = "Validate" });

            statusLabel = new Label("Select or create a Promo Template Graph asset.");
            statusLabel.style.marginLeft = 8;
            statusLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            toolbar.Add(statusLabel);
            rootVisualElement.Add(toolbar);

            graphView = new PromoGraphView(OnGraphChanged);
            graphView.style.flexGrow = 1;
            rootVisualElement.Add(graphView);
            if (graphAsset != null) graphView.Load(graphAsset);
        }

        private void CreateGraphAsset()
        {
            var path = EditorUtility.SaveFilePanelInProject(
                "Create Promo Template Graph", "NewPromoTemplate", "asset", "Choose where to save the promo graph.");
            if (string.IsNullOrEmpty(path)) return;

            var asset = CreateInstance<PromoTemplateGraphAsset>();
            asset.TemplateId = $"promo_template_{Guid.NewGuid():N}";
            asset.KoreanName = System.IO.Path.GetFileNameWithoutExtension(path);
            asset.EnglishName = asset.KoreanName;
            var start = new PromoGraphNodeData
            {
                Id = Guid.NewGuid().ToString("N"), Type = PromoGraphNodeType.Start,
                Title = "Start", Position = new Vector2(100f, 200f)
            };
            var outcome = new PromoGraphNodeData
            {
                Id = Guid.NewGuid().ToString("N"), Type = PromoGraphNodeType.Outcome,
                Title = "Outcome", Position = new Vector2(500f, 200f)
            };
            asset.Nodes.Add(start);
            asset.Nodes.Add(outcome);
            asset.Edges.Add(new PromoGraphEdgeData
            {
                Id = Guid.NewGuid().ToString("N"), FromNodeId = start.Id, ToNodeId = outcome.Id
            });
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = asset;
            SetAsset(asset);
        }

        private DropdownMenuAction.Status CanAdd(PromoGraphNodeType type)
        {
            if (graphAsset == null) return DropdownMenuAction.Status.Disabled;
            if (type == PromoGraphNodeType.Start && graphAsset.Nodes.Any(x => x?.Type == PromoGraphNodeType.Start))
                return DropdownMenuAction.Status.Disabled;
            return DropdownMenuAction.Status.Normal;
        }

        private void SetAsset(PromoTemplateGraphAsset asset)
        {
            graphAsset = asset;
            if (graphView != null) graphView.Load(asset);
            if (statusLabel != null) statusLabel.text = asset == null ? "Select or create a Promo Template Graph asset." : asset.DisplayName;
        }

        private void AddNode(PromoGraphNodeType type)
        {
            if (graphAsset == null) return;
            Undo.RecordObject(graphAsset, "Add promo graph node");
            var data = new PromoGraphNodeData
            {
                Id = Guid.NewGuid().ToString("N"),
                Type = type,
                Title = type.ToString(),
                Position = graphView.GetDefaultNodePosition()
            };
            graphAsset.Nodes.Add(data);
            graphView.AddNode(data);
            MarkDirty();
        }

        private void OnGraphChanged() => MarkDirty();

        private void MarkDirty()
        {
            if (graphAsset == null) return;
            EditorUtility.SetDirty(graphAsset);
            statusLabel.text = $"{graphAsset.DisplayName} *";
        }

        private void Save()
        {
            if (graphAsset == null) return;
            graphView.WritePositions();
            EditorUtility.SetDirty(graphAsset);
            AssetDatabase.SaveAssets();
            statusLabel.text = $"Saved: {graphAsset.DisplayName}";
        }

        private void ValidateGraph()
        {
            if (graphAsset == null) return;
            graphView.WritePositions();
            var errors = PromoTemplateGraphValidator.Validate(graphAsset);
            statusLabel.text = errors.Count == 0 ? "Valid graph" : $"Validation: {errors.Count} issue(s)";
            if (errors.Count == 0) Debug.Log($"Promo graph is valid: {graphAsset.name}", graphAsset);
            else Debug.LogWarning($"Promo graph validation failed: {graphAsset.name}\n- {string.Join("\n- ", errors)}", graphAsset);
        }
    }

    internal sealed class PromoGraphView : GraphView
    {
        private readonly Action changed;
        private PromoTemplateGraphAsset asset;
        private bool loading;

        public PromoGraphView(Action changed)
        {
            this.changed = changed;
            style.flexGrow = 1;
            Insert(0, new GridBackground());
            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());
            graphViewChanged = HandleGraphViewChanged;
        }

        public void Load(PromoTemplateGraphAsset graphAsset)
        {
            loading = true;
            DeleteElements(graphElements.ToList());
            asset = graphAsset;
            if (asset != null)
            {
                asset.Nodes ??= new List<PromoGraphNodeData>();
                asset.Edges ??= new List<PromoGraphEdgeData>();
                foreach (var node in asset.Nodes.Where(x => x != null)) AddNode(node);
                foreach (var edgeData in asset.Edges.Where(x => x != null))
                {
                    var source = GetNodeByGuid(edgeData.FromNodeId) as PromoGraphNodeView;
                    var target = GetNodeByGuid(edgeData.ToNodeId) as PromoGraphNodeView;
                    if (source?.Output == null || target?.Input == null) continue;
                    var edge = source.Output.ConnectTo(target.Input);
                    edge.userData = edgeData.Id;
                    AddElement(edge);
                }
            }
            loading = false;
        }

        public void AddNode(PromoGraphNodeData data) => AddElement(new PromoGraphNodeView(data, changed));

        public Vector2 GetDefaultNodePosition()
        {
            var center = contentViewContainer.WorldToLocal(worldBound.center);
            return center + new Vector2(UnityEngine.Random.Range(-30f, 30f), UnityEngine.Random.Range(-30f, 30f));
        }

        public void WritePositions()
        {
            foreach (var node in nodes.OfType<PromoGraphNodeView>())
                node.Data.Position = node.GetPosition().position;
        }

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter) =>
            ports.ToList().Where(port => port != startPort && port.node != startPort.node && port.direction != startPort.direction).ToList();

        private GraphViewChange HandleGraphViewChanged(GraphViewChange change)
        {
            if (loading || asset == null) return change;
            Undo.RecordObject(asset, "Edit promo graph");

            if (change.edgesToCreate != null)
            {
                foreach (var edge in change.edgesToCreate)
                {
                    var source = edge.output.node as PromoGraphNodeView;
                    var target = edge.input.node as PromoGraphNodeView;
                    if (source == null || target == null) continue;
                    var edgeData = new PromoGraphEdgeData
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        FromNodeId = source.Data.Id,
                        ToNodeId = target.Data.Id
                    };
                    edge.userData = edgeData.Id;
                    asset.Edges.Add(edgeData);
                }
            }

            if (change.elementsToRemove != null)
            {
                var removedNodes = change.elementsToRemove.OfType<PromoGraphNodeView>().Select(x => x.Data.Id).ToHashSet();
                var removedEdges = change.elementsToRemove.OfType<Edge>().Select(x => x.userData as string).Where(x => x != null).ToHashSet();
                asset.Nodes.RemoveAll(x => x != null && removedNodes.Contains(x.Id));
                asset.Edges.RemoveAll(x => x != null && (removedEdges.Contains(x.Id) || removedNodes.Contains(x.FromNodeId) || removedNodes.Contains(x.ToNodeId)));
            }

            if (change.movedElements != null) WritePositions();
            changed();
            return change;
        }
    }

    internal sealed class PromoGraphNodeView : Node
    {
        public PromoGraphNodeData Data { get; }
        public Port Input { get; }
        public Port Output { get; }

        public PromoGraphNodeView(PromoGraphNodeData data, Action changed)
        {
            Data = data;
            viewDataKey = data.Id;
            title = data.Type.ToString();
            SetPosition(new Rect(data.Position, new Vector2(230f, 150f)));

            if (data.Type != PromoGraphNodeType.Start)
            {
                Input = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(bool));
                Input.portName = "In";
                inputContainer.Add(Input);
            }
            if (data.Type != PromoGraphNodeType.Outcome)
            {
                Output = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(bool));
                Output.portName = "Out";
                outputContainer.Add(Output);
            }

            var titleField = new TextField("Title") { value = data.Title };
            titleField.RegisterValueChangedCallback(evt => { data.Title = evt.newValue; changed(); });
            extensionContainer.Add(titleField);
            var descriptionField = new TextField("Description") { value = data.Description, multiline = true };
            descriptionField.RegisterValueChangedCallback(evt => { data.Description = evt.newValue; changed(); });
            extensionContainer.Add(descriptionField);
            RefreshExpandedState();
            RefreshPorts();
        }
    }

    [CustomEditor(typeof(PromoTemplateGraphAsset))]
    public sealed class PromoTemplateGraphAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (GUILayout.Button("Open Promo Graph Editor"))
                PromoGraphEditorWindow.Open((PromoTemplateGraphAsset)target);
        }
    }
}
