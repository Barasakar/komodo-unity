using UnityEngine;
using Unity.Entities;
using System.Collections.Generic;
using System;
using Komodo.Utilities;

namespace Komodo.Runtime
{
    public class DrawingInstanceManager : SingletonComponent<DrawingInstanceManager>
    {
        public static DrawingInstanceManager Instance
        {
            get { return (DrawingInstanceManager) _Instance; }

            set { _Instance = value; }
        }

        public Transform lineRendererContainerPrefab;

        public EntityManager entityManager;

        [HideInInspector] public Transform userStrokeParent;

        [HideInInspector] public Transform externalStrokeParent;

        private Dictionary<int, LineRenderer> lineRenderersInQueue = new Dictionary<int, LineRenderer>();

        private Dictionary<int, int> allStrokeIDValidator = new Dictionary<int, int>();

        public void Awake()
        {
            //used to set our managers alive state to true to detect if it exist within scene
            var initManager = Instance;

            //TODO -- warn if we are not attached to a GameObject

            entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

            //parent for our stored lines
            userStrokeParent = new GameObject("UserStrokeParent").transform;

            externalStrokeParent = new GameObject("ExternalClientStrokeParent").transform;

            userStrokeParent.SetParent(transform);

            externalStrokeParent.SetParent(transform);

            GlobalMessageManager.Instance.Subscribe("draw", (str) => Draw_Refresh(str));
        }

        public void CreateUserStrokeInstance(int strokeID, LineRenderer lineRenderer, bool sendNetworkCall)
        {
            //set correct pivot point when scaling object by grabbing
            GameObject pivot = new GameObject("LineRender:" + strokeID, typeof(BoxCollider));

            if (lineRendererContainerPrefab == null)
            {
                throw new System.Exception("Line Renderer Container Prefab is not assigned in DrawingInstanceManager");
            }

            GameObject lineRendCopy = Instantiate(lineRendererContainerPrefab).gameObject;

            lineRendCopy.name = "LineR:" + strokeID;

            //Create a reference to use in network
            var nAGO = NetworkedObjectsManager.Instance.CreateNetworkedGameObject(pivot, strokeID, strokeID);

            // Make own client's draw strokes grabbable
            pivot.tag = TagList.interactable;

            entityManager.AddComponentData(nAGO.Entity, new DrawingTag { });

            var bColl = pivot.GetComponent<BoxCollider>();

            LineRenderer copiedLR = lineRendCopy.GetComponent<LineRenderer>();

            var color = lineRenderer.startColor;

            copiedLR.startColor = color;

            copiedLR.endColor = color;

            copiedLR.widthMultiplier = lineRenderer.widthMultiplier;

            Bounds newBounds = new Bounds(lineRenderer.GetPosition(0), Vector3.one * 0.01f);

            copiedLR.positionCount = 0;

            for (int i = 0; i < lineRenderer.positionCount; i++)
            {
                copiedLR.positionCount++;

                copiedLR.SetPosition(i, lineRenderer.GetPosition(i));

                newBounds.Encapsulate(new Bounds(lineRenderer.GetPosition(i), Vector3.one * 0.01f));
            }

            pivot.transform.position = newBounds.center;

            bColl.center = lineRendCopy.transform.position;

            bColl.size = newBounds.size;

            lineRendCopy.transform.SetParent(pivot.transform, true);

            if (sendNetworkCall)
            {
                SendStrokeNetworkUpdate(
                    strokeID,
                    Entity_Type.LineEnd,
                    copiedLR.widthMultiplier,
                    lineRenderer.GetPosition(lineRenderer.positionCount - 1),
                    new Vector4(lineRenderer.startColor.r, lineRenderer.startColor.g, lineRenderer.startColor.b,  lineRenderer.startColor.a)
                );
            }

            pivot.transform.SetParent(userStrokeParent, true);

            if (UndoRedoManager.IsAlive)
            {
                //save undoing process for ourselves and others
                UndoRedoManager.Instance.savedStrokeActions.Push(() =>
                {
                    pivot.SetActive(false);

                    SendStrokeNetworkUpdate(strokeID, Entity_Type.LineNotRender);
                });
            }
        }

        public void CreateExternalClientStrokeInstance(int strokeID, LineRenderer currentLineRenderer)
        {
            GameObject pivot = new GameObject("LineRender:" + strokeID, typeof(BoxCollider));

            NetworkedGameObject nAGO = NetworkedObjectsManager.Instance.CreateNetworkedGameObject(pivot, strokeID, strokeID, true);

            // Make other clients' draw strokes grabbable
            pivot.tag = TagList.interactable;

            //tag created drawing object will be useful in the future for having items with multiple tags
            entityManager.AddComponentData(nAGO.Entity, new DrawingTag { });

            var bColl = pivot.GetComponent<BoxCollider>();

            Bounds newBounds = new Bounds(currentLineRenderer.GetPosition(0), Vector3.one * 0.01f);

            for (int i = 0; i < currentLineRenderer.positionCount; i++)
            {
                newBounds.Encapsulate(new Bounds(currentLineRenderer.GetPosition(i), Vector3.one * 0.01f));
            }

            pivot.transform.position = newBounds.center;

            bColl.center = currentLineRenderer.transform.position;

            bColl.size = newBounds.size;

            currentLineRenderer.transform.SetParent(pivot.transform, true);

            pivot.transform.SetParent(externalStrokeParent, true);
        }

        public void SendStrokeNetworkUpdate(int sID, Entity_Type entityType, float lineWidth = 1, Vector3 curPos = default, Vector4 color = default)
        {
            var drawUpdate = new Draw((int) NetworkUpdateHandler.Instance.client_id, sID, (int) entityType, lineWidth, curPos, color);

            var drawSer = JsonUtility.ToJson(drawUpdate);

            KomodoMessage komodoMessage = new KomodoMessage("draw", drawSer);

            komodoMessage.Send();
        }

        public void Draw_Refresh (string stringData)
        {
            Draw newData = JsonUtility.FromJson<Draw>(stringData);

            LineRenderer currentLineRenderer = default;

            //we start a new line if there is no ID already corresponding to one in the scene
            if (!allStrokeIDValidator.ContainsKey(newData.strokeId))
            {
                GameObject lineRendCopy = Instantiate(DrawingInstanceManager.Instance.lineRendererContainerPrefab).gameObject;

                lineRendCopy.name = "LineR:" + newData.strokeId;

                lineRendCopy.transform.SetParent(DrawingInstanceManager.Instance.externalStrokeParent, true);

                currentLineRenderer = lineRendCopy.GetComponent<LineRenderer>();

                currentLineRenderer.positionCount = 0;

                allStrokeIDValidator.Add(newData.strokeId, newData.strokeId);

                lineRenderersInQueue.Add(newData.strokeId, currentLineRenderer);
            }

            // get reference to the line renderer we are supposed to be working with 
            if (lineRenderersInQueue.ContainsKey(newData.strokeId))
            {
                currentLineRenderer = lineRenderersInQueue[newData.strokeId];
            }

            switch (newData.strokeType)
            {
                // Continues a Line
                case (int) Entity_Type.Line:
                {
                    var brushColor = new Vector4(newData.curColor.x, newData.curColor.y, newData.curColor.z, newData.curColor.w);

                    currentLineRenderer.startColor = brushColor;

                    currentLineRenderer.endColor = brushColor;

                    currentLineRenderer.widthMultiplier = newData.lineWidth;

                    ++currentLineRenderer.positionCount;

                    currentLineRenderer.SetPosition(currentLineRenderer.positionCount - 1, newData.curStrokePos);

                    break;
                }

                case (int) Entity_Type.LineEnd:
                {
                    ++currentLineRenderer.positionCount;

                    currentLineRenderer.SetPosition(currentLineRenderer.positionCount - 1, newData.curStrokePos);

                    DrawingInstanceManager.Instance.CreateExternalClientStrokeInstance(newData.strokeId, currentLineRenderer);

                    break;
                }

                case (int) Entity_Type.LineDelete:
                {
                    if (NetworkedObjectsManager.Instance.networkedObjectFromEntityId.ContainsKey(newData.strokeId))
                    {
                        if (lineRenderersInQueue.ContainsKey(newData.strokeId))
                        {
                            lineRenderersInQueue.Remove(newData.strokeId);
                        }

                        Destroy(NetworkedObjectsManager.Instance.networkedObjectFromEntityId[newData.strokeId].gameObject);

                        NetworkedObjectsManager.Instance.networkedObjectFromEntityId.Remove(newData.strokeId);
                    }
                    break;
                }

                case (int) Entity_Type.LineRender:
                {
                    if (NetworkedObjectsManager.Instance.networkedObjectFromEntityId.ContainsKey(newData.strokeId))
                    {
                        NetworkedObjectsManager.Instance.networkedObjectFromEntityId[newData.strokeId].gameObject.SetActive(true);
                    }

                    break;
                }

                case (int) Entity_Type.LineNotRender:
                {
                    if (NetworkedObjectsManager.Instance.networkedObjectFromEntityId.ContainsKey(newData.strokeId))
                    {
                        NetworkedObjectsManager.Instance.networkedObjectFromEntityId[newData.strokeId].gameObject.SetActive(false);
                    }

                    break;
                }
            }
        }
    }
}
