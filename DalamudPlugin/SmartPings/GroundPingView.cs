using Dalamud.Game;
using Dalamud.Game.Addon.Events;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Gui;
using Dalamud.Interface.Utility;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Arrays;
using FFXIVClientStructs.FFXIV.Common.Component.Excel;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using Lumina.Excel.Sheets;
using Lumina.Excel.Sheets.Experimental;
using Newtonsoft.Json;
using SmartPings.Extensions;
using SmartPings.Input;
using SmartPings.Log;
using SmartPings.UI.Util;
using SmartPings.UI.View;
using System;
using System.Numerics;
using System.Reactive.Subjects;
using System.Runtime.CompilerServices;

namespace SmartPings;

public unsafe class GroundPingView : IPluginUIView, IDisposable
{
    // this extra bool exists for ImGui, since you can't ref a property
    private bool visible = false;
    public bool Visible
    {
        get => this.visible;
        set => this.visible = value;
    }

    public IObservable<GroundPing> AddGroundPing => addGroundPing;
    private readonly Subject<GroundPing> addGroundPing = new();

    private readonly Lazy<GroundPingPresenter> presenter;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IClientState clientState;
    private readonly IGameGui gameGui;
    private readonly ITextureProvider textureProvider;
    private readonly IKeyState keyState;
    private readonly IAddonLifecycle addonLifecycle;
    private readonly IAddonEventManager addonEventManager;
    private readonly IDataManager dataManager;
    private readonly InputEventSource inputEventSource;
    private readonly Configuration configuration;
    private readonly MapManager mapManager;
    private readonly ILogger logger;

    public StatusSheet statusSheet;

    private bool leftMouseUpThisFrame;
    private bool createPingOnLeftMouseUp;

    private Matrix4x4 viewProj;
    private Vector4 nearPlane;

    public GroundPingView(
        Lazy<GroundPingPresenter> presenter,
        IDalamudPluginInterface pluginInterface,
        IClientState clientState,
        IGameGui gameGui,
        ITextureProvider textureProvider,
        IKeyState keyState,
        IAddonLifecycle addonLifecycle,
        IAddonEventManager addonEventManager,
        IDataManager dataManager,
        IFramework framework,
        IChatGui chatGui,
        InputEventSource inputEventSource,
        Configuration configuration,
        MapManager mapManager,
        ILogger logger)
    {
        this.presenter = presenter;
        this.pluginInterface = pluginInterface;
        this.clientState = clientState;
        this.gameGui = gameGui;
        this.textureProvider = textureProvider;
        this.keyState = keyState;
        this.addonLifecycle = addonLifecycle;
        this.addonEventManager = addonEventManager;
        this.dataManager = dataManager;
        this.inputEventSource = inputEventSource;
        this.configuration = configuration;
        this.mapManager = mapManager;
        this.logger = logger;

        //XivAlexander will crash if an ExcelSheet instance is accessed outside of the thread it is created in.
        this.statusSheet = new StatusSheet(dataManager.GetExcelSheet<Lumina.Excel.Sheets.Status>(clientState.ClientLanguage));
        if (this.statusSheet.Count == 0)
        {
            this.logger.Error("Could not load Status Excel Sheet. UI pings will not work.");
        }

        this.inputEventSource.SubscribeToKeyDown(args =>
        {
            //if (!this.configuration.EnablePingInput)
            //{
            //    return;
            //}

            if (args.Key == WindowsInput.Events.KeyCode.LButton &&
                this.keyState.IsVirtualKeyValid(this.configuration.QuickPingKeybind) &&
                this.keyState.GetRawValue(this.configuration.QuickPingKeybind) > 0)
            {
                if (ImGui.IsWindowHovered(ImGuiHoveredFlags.AnyWindow))
                {
                    return;
                }

                var collisionNode = AtkStage.Instance()->AtkCollisionManager->IntersectingCollisionNode;
                if (collisionNode != null && !collisionNode->NodeFlags.HasFlag(NodeFlags.UseDepthBasedPriority))
                {
                    this.logger.Info("Mouse over collision node {0}", ((nint)collisionNode).ToString("X"));

                    // Statuses here are sorted first by PartyListPriority, and then by time-acquired
                    //foreach (var statusId in AgentHUD.Instance()->Status->StatusIds)
                    //{
                    //    if (statusId == 0) { continue; }
                    //    this.logger.Info("AgentHUD: Status id {0}", statusId);
                    //    if (this.statusSheet.TryGetStatusById(statusId, out var status))
                    //    {
                    //        this.logger.Info("AgentHUD: Status {0}", JsonConvert.SerializeObject(status).ToString());
                    //    }
                    //}
                    // Same order as HUDAgent, doesn't provide extra info
                    //HudNumberArray.Instance()->StatusIconIds
                    // Seemingly random statuses, not useful
                    //Hud2NumberArray.Instance()->TargetStatusIconIds

                    // On the UI, own statuses are always sorted by PartyListPriority then time-acquired
                    // _StatusCustom0 contains Enhancements, which have StatusCategory 1, CanIncreaseRewards 0
                    // _StatusCustom1 contains Enfeeblements, which have StatusCategory 2
                    // _StatusCustom2 contains Other, which have StatusCategory 1, CanIncreaseRewards 1
                    // _StatusCustom3 contains Conditional Enhancements, which have StatusCategory 1, CanIncreaseRewards 2
                    // If _StatusCustom3 does not exist, statuses that would normally go there go to _StatusCustom0 instead

                    if (TryGetStatusNameOfCollisionNode(collisionNode, out var name))
                    {
                        this.logger.Info("Collision node is for status {0}", name!);
                        chatGui.Print($"Status: {name}");
                    }

                    // The PartyMembers array always has 10 slots, but accessing an index at or above PartyMemberCount
                    // will crash XivAlexander
                    for(var i = 0; i < AgentHUD.Instance()->PartyMemberCount; i++)
                    {
                        var partyMember = AgentHUD.Instance()->PartyMembers[i];
                        // These include Other statuses
                        // These seem randomly sorted, but statuses with the same PartyListPriority are
                        // sorted relative to each other
                        foreach (var status in partyMember.Object->StatusManager.Status)
                        {
                            if (status.StatusId == 0) { continue; }
                            this.statusSheet.TryGetStatusById(status.StatusId, out var s);
                            this.logger.Info("Party member {0}, index {1}, has status {2}",
                               partyMember.Object->NameString, partyMember.Index, JsonConvert.SerializeObject(s).ToString());
                        }
                    }

                    // IClientState.LocalPlayer must be accessed in a Framework thread (or else XivAlexander will crash)
                    //framework.Run(() =>
                    //{
                    //    if (this.clientState.LocalPlayer != null)
                    //    {
                    //        // These statuses are sorted in time-acquired-order
                    //        for (var i = 0; i < this.clientState.LocalPlayer.StatusList.Length; i++)
                    //        {
                    //            var playerStatus = this.clientState.LocalPlayer.StatusList[i];
                    //            if (playerStatus != null && playerStatus.StatusId > 0 &&
                    //                this.statusSheet.TryGetStatusById(playerStatus.StatusId, out var status))
                    //            {
                    //                this.logger.Info("Player has status with index {0}, id {1}, name {2}, icon {3}", i, playerStatus.StatusId, status.Name, status.Icon);
                    //            }
                    //        }
                    //    }
                    //    // IGameGui.HoveredAction does not update to 0 when not hovering over anything
                    //});
                    return;
                }

                // Both lines are needed to consistently capture mouse input
                ImGui.GetIO().WantCaptureMouse = true;
                ImGui.SetNextFrameWantCaptureMouse(true);
                createPingOnLeftMouseUp = true;
            }
        });
        this.inputEventSource.SubscribeToKeyUp(args =>
        {
            if (args.Key == WindowsInput.Events.KeyCode.LButton)
            {
                leftMouseUpThisFrame = true;
            }
        });

        //this.gameGui.HoveredActionChanged += OnHoveredActionChanged;
        //this.addonLifecycle.RegisterListener(AddonEvent.PostDraw, OnAddonEvent);
    }

    private bool TryGetStatusNameOfCollisionNode(AtkCollisionNode* collisionNode, out string? name)
    {
        name = null;
        if (collisionNode == null) { return false; }
        //if (this.clientState.LocalPlayer == null) { return false; }

        var imageNode = collisionNode->GetAsAtkImageNode();
        if (imageNode == null) { return false; }

        var componentNode = collisionNode->ParentNode;
        if (componentNode == null) { return false; }

        // Found through testing: _StatusCustom component nodes are of Type 1001
        if ((ushort)componentNode->Type != 1001) { return false; }

        var statusSheet = this.dataManager.GetExcelSheet<Lumina.Excel.Sheets.Status>(clientState.ClientLanguage);
        if (statusSheet == null)
        {
            this.logger.Error("Could not load Status Excel Sheet. UI pings will not work.");
            return false;
        }

        var partsList = imageNode->PartsList; if (partsList == null) { return false; }
        var parts = partsList->Parts; if (parts == null) { return false; }
        var uldAsset = parts->UldAsset; if (uldAsset == null) { return false; }
        var resource = uldAsset->AtkTexture.Resource; if (resource == null) { return false; }
        var iconId = resource->IconId;
        this.logger.Info("Collision node icon id is {0}", iconId);
        if (this.statusSheet.TryGetStatusByIcon(iconId, out var status))
        {
            this.logger.Info("Collision node status id is {0}", status.Id);
            name = status.Name;
            return true;
        }

        return false;
    }

    public void Dispose()
    {
        //this.gameGui.HoveredActionChanged -= OnHoveredActionChanged;
        //this.addonLifecycle.UnregisterListener(OnAddonEvent);
        //foreach (var handle in this.addonEventHandles.Values)
        //{
        //    this.addonEventManager.RemoveEvent(handle);
        //}
        GC.SuppressFinalize(this);
    }

    //private void OnHoveredActionChanged(object? sender, HoveredAction hoveredAction)
    //{
    //    this.logger.Info("Hovered over action changed to {0}, {1}", this.gameGui.HoveredAction.ActionID, this.gameGui.HoveredAction.ActionKind);
    //}

    //private void OnAddonEvent(AddonEvent type, AddonArgs args)
    //{
    //    var addon = (AtkUnitBase*)args.Addon;

    //    var iconTextNode = addon->GetComponentByNodeId(2);
    //    if (iconTextNode == null) { return; }

    //    var imageNode = iconTextNode->GetImageNodeById(3);
    //    if (imageNode == null) { return; }

    //    if (!addonEventHandles.ContainsKey((nint)imageNode))
    //    {
    //        var handle = this.addonEventManager.AddEvent((nint)addon, (nint)imageNode, AddonEventType.MouseOver, OnMouseOver);
    //        if (handle != null)
    //        {
    //            addonEventHandles.Add((nint)imageNode, handle);
    //        }
    //    }
    //}

    //private void OnMouseOver(AddonEventType type, IntPtr addonPtr, IntPtr nodePtr)
    //{
    //    var addon = (AtkUnitBase*)addonPtr;
    //    var node = (AtkResNode*)nodePtr;
    //    this.logger.Info("Mouse over addon {0}, node {1}", addon->NameString, nodePtr.ToString());
    //}

    public void Draw()
    {
        if (this.presenter.Value == null) { return; }

        if (createPingOnLeftMouseUp)
        {
            ImGui.SetNextFrameWantCaptureMouse(true);
        }

        if (clientState.LocalPlayer != null)
        {
            if (createPingOnLeftMouseUp && leftMouseUpThisFrame)
            {
                if (this.gameGui.ScreenToWorld(ImGui.GetMousePos(), out var worldPos))
                {
                    this.logger.Debug("Clicked on world position {0} for ground ping", worldPos);
                    var ping = new GroundPing
                    {
                        PingType = GroundPing.Type.Basic,
                        StartTimestamp = DateTime.UtcNow.Ticks,
                        Author = this.clientState.LocalPlayer.Name.TextValue,
                        MapId = this.mapManager.GetCurrentMapPublicRoomName(),
                        WorldPosition = worldPos,
                    };
                    this.addGroundPing.OnNext(ping);
                }
                createPingOnLeftMouseUp = false;
            }

            if (!DrawPings())
            {
                this.presenter.Value.GroundPings.Clear();
            }
        }
        else
        {
            this.presenter.Value.GroundPings.Clear();
        }

        // Pictomancy testing
        //PctDrawHints hints = new(drawWithVfx: true);
        //using (var drawList = PictoService.Draw(hints: hints))
        //using (var drawList = PictoService.Draw())
        //{
        //    if (drawList == null)
        //    {
        //        return;
        //    }

        //    if (clientState.LocalPlayer != null)
        //    {
        //        using (drawList.PushDrawContext($"{clientState.LocalPlayer.EntityId}"))
        //        {
        //            // Draw a circle
        //            var worldPosition = clientState.LocalPlayer.Position;
        //            var radius = clientState.LocalPlayer.HitboxRadius;

        //            drawList.AddCircleFilled(worldPosition, 2 * radius, ImGui.ColorConvertFloat4ToU32(Vector4Colors.Green));
        //            //drawList.AddCircle(worldPosition, radius, ImGui.ColorConvertFloat4ToU32(ImGuiColors.DalamudRed));
        //            //PictoService.GameGui.WorldToScreen(worldPosition, out var sp);
        //            //ImGui.SetCursorPos(screenPos);
        //            //var img = this.textureProvider.GetFromFile(this.pluginInterface.GetResourcePath("question.png")).GetWrapOrDefault()?.ImGuiHandle ?? default;
        //            //ImGui.Image(i, new(100, 100));
        //            //var imageSize = new Vector2(150, 150);
        //            //ImGui.GetForegroundDrawList().AddImage(img, sp - imageSize / 2, sp + imageSize / 2);
        //        }
        //    }
        //}

        leftMouseUpThisFrame = false;
    }

    private bool DrawPings()
    {
        // Setup draw matrices (taken from Pictomancy)
        viewProj = Control.Instance()->ViewProjectionMatrix;

        // The view matrix in CameraManager is 1 frame stale compared to the Control viewproj matrix.
        // Computing the near plane using the stale view matrix results in clipping errors that look really bad when moving the camera.
        // Instead, compute the view matrix using the accurate viewproj matrix multiplied by the stale inverse proj matrix (Which rarely changes)
        var controlCamera = Control.Instance()->CameraManager.GetActiveCamera();
        var renderCamera = controlCamera != null ? controlCamera->SceneCamera.RenderCamera : null;
        if (renderCamera == null)
        {
            return false;
        }
        var Proj = renderCamera->ProjectionMatrix;
        if (!Matrix4x4.Invert(Proj, out var InvProj))
        {
            return false;
        }
        var View = viewProj * InvProj;

        nearPlane = new(View.M13, View.M23, View.M33, View.M43 + renderCamera->NearPlane);

        var mapId = this.mapManager.GetCurrentMapPublicRoomName();
        var pNode = this.presenter.Value.GroundPings.First;
        while (pNode != null)
        {
            var p = pNode.Value;
            var nextNode = pNode.Next;

            if (mapId != p.MapId)
            {
                this.presenter.Value.GroundPings.Remove(pNode);
                pNode = nextNode;
                continue;
            }

            bool pingDrawn = false;
            switch (p.PingType)
            {
                case GroundPing.Type.Basic:
                    pingDrawn = DrawBasicPing(ImGui.GetForegroundDrawList(), p.WorldPosition, 1.0f, p.DrawDuration, p.Author);
                    break;
                    //case GroundPing.Type.Question:
                    //    pingDrawn = DrawQuestionMarkPing(ImGui.GetForegroundDrawList(), screenPos, new(250, 250), p.DrawDuration, p.Author);
                    //    break;
            }

            if (!pingDrawn)
            {
                this.presenter.Value.GroundPings.Remove(pNode);
                pNode = nextNode;
                continue;
            }
            p.DrawDuration += ImGui.GetIO().DeltaTime;

            // Extra cleanup
            if (p.DrawDuration > 10)
            {
                this.presenter.Value.GroundPings.Remove(pNode);
                pNode = nextNode;
                continue;
            }

            pNode = nextNode;
        }

        return true;
    }

    private bool DrawBasicPing(ImDrawListPtr drawList, Vector3 worldPosition, float scale, float time, string? author)
    {
        bool draw = false;
        var onScreen = this.gameGui.WorldToScreen(worldPosition, out var screenPosition);

        // Calculate screen size based on world position
        var hForward = Vector3.Normalize(new Vector3(nearPlane.X, 0, nearPlane.Z));
        var hRight = Vector3.Transform(hForward, Matrix4x4.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2));
        var wpForward = worldPosition + scale * hForward;
        var wpRight = worldPosition + scale * hRight;
        var wpBack = worldPosition - scale * hForward;
        var wpLeft = worldPosition - scale * hRight;
        this.gameGui.WorldToScreen(wpForward, out var spForward);
        this.gameGui.WorldToScreen(wpRight, out var spRight);
        this.gameGui.WorldToScreen(wpBack, out var spBack);
        this.gameGui.WorldToScreen(wpLeft, out var spLeft);

        var ringSize = new Vector2(MathF.Abs(spRight.X - spLeft.X), MathF.Abs(spForward.Y - spBack.Y));
        // Clamp to min and max sizes if necessary
        if (ringSize.X < 70)
        {
            ringSize.Y *= 70 / ringSize.X;
            ringSize.X = 70;
        }
        if (ringSize.X > 800)
        {
            ringSize.Y *= 800 / ringSize.X;
            ringSize.X = 800;
        }

        {
            var imgRing = this.textureProvider.GetFromFile(this.pluginInterface.GetResourcePath("basic_ping_ring_sheet.png")).GetWrapOrDefault()?.ImGuiHandle ?? default;
            float fps = 30;
            int rowCount = 5;
            int colCount = 7;
            int totalFrames = 35;
            int totalWidth = 1792;
            int totalHeight = 1280;

            int frame = (int)(time * fps);
            if (frame < totalFrames)
            {
                if (onScreen)
                {
                    int width = totalWidth / colCount;
                    int height = totalHeight / rowCount;

                    int row = frame / colCount;
                    int col = frame % colCount;

                    var uv0 = new Vector2((float)col / colCount, (float)row / rowCount);
                    var uv1 = uv0 + new Vector2((float)width / totalWidth, (float)height / totalHeight);

                    var p0 = new Vector2(screenPosition.X - ringSize.X / 2, screenPosition.Y - ringSize.Y / 2);
                    var p1 = new Vector2(screenPosition.X + ringSize.X / 2, screenPosition.Y + ringSize.Y / 2);

                    drawList.AddImage(imgRing, p0, p1, uv0, uv1);
                }
                draw = true;
            }
        }

        {
            var imgPing = this.textureProvider.GetFromFile(this.pluginInterface.GetResourcePath("basic_ping_sheet.png")).GetWrapOrDefault()?.ImGuiHandle ?? default;
            float fps = 30;
            int rowCount = 4;
            int colCount = 4;
            int totalFrames = 60;
            int totalWidth = 2048;
            int totalHeight = 2048;
            int frame0HoldFrames = 46;

            int frame = (int)(time * fps);
            if (frame < totalFrames)
            {
                if (onScreen)
                {
                    if (frame < frame0HoldFrames)
                    {
                        frame = 0;

                        // Draw author tag
                        // TODO: Factor in Dalamud global font scale
                        //var fontSize = 20;
                        var fontSize = Math.Clamp(0.125f * ringSize.X, 17, 30);
                        var textSize = ImGui.CalcTextSize(author) * fontSize / 16;
                        var minPad = new Vector2(-5, 0);
                        drawList.AddRectFilled(screenPosition + minPad, screenPosition + textSize, Vector4Colors.Black.WithAlpha(0.6f).ToColorU32());
                        drawList.AddText(ImGui.GetFont(), fontSize, screenPosition, Vector4Colors.White.ToColorU32(), author);
                    }
                    else
                    {
                        frame -= (frame0HoldFrames - 1);
                    }

                    int width = totalWidth / colCount;
                    int height = totalHeight / rowCount;

                    int row = frame / colCount;
                    int col = frame % colCount;

                    var uv0 = new Vector2((float)col / colCount, (float)row / rowCount);
                    var uv1 = uv0 + new Vector2((float)width / totalWidth, (float)height / totalHeight);

                    var basicPingSize = ringSize.X * 0.5f;
                    var p0 = new Vector2(screenPosition.X - basicPingSize / 2, screenPosition.Y - basicPingSize);
                    var p1 = new Vector2(screenPosition.X + basicPingSize / 2, screenPosition.Y);
                    drawList.AddImage(imgPing, p0, p1, uv0, uv1);
                }
                draw = true;
            }
        }

        return draw;
    }

    //private bool DrawQuestionMarkPing(ImDrawListPtr drawList, Vector2 position, Vector2 size, float time, string? author)
    //{
    //    var img = this.textureProvider.GetFromFile(this.pluginInterface.GetResourcePath("question_sheet.png")).GetWrapOrDefault()?.ImGuiHandle ?? default;
    //    float fps = 45;
    //    int rowCount = 5;
    //    int colCount = 11;
    //    int totalFrames = 54;
    //    int totalWidth = 7700;
    //    int totalHeight = 3500;

    //    int frame = (int)(time * fps); // 0-index
    //    if (frame >= totalFrames)
    //    {
    //        return false;
    //    }

    //    int width = totalWidth / colCount;
    //    int height = totalHeight / rowCount;

    //    int row = frame / colCount;
    //    int col = frame % colCount;

    //    var uv0 = new Vector2((float)col / colCount, (float)row / rowCount);
    //    var uv1 = uv0 + new Vector2((float)width / totalWidth, (float)height / totalHeight);

    //    drawList.AddImage(img, position - size / 2, position + size / 2, uv0, uv1);

    //    var fontSize = 20;
    //    var textSize = ImGui.CalcTextSize(author) * fontSize / 16;
    //    var minPad = new Vector2(-5, 0);
    //    drawList.AddRectFilled(position + minPad, position + textSize, Vector4Colors.Black.WithAlpha(0.6f).ToColorU32());
    //    drawList.AddText(ImGui.GetFont(), fontSize, position, Vector4Colors.White.ToColorU32(), author);

    //    return true;
    //}

    // Taken from Pictomancy
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TransformCoordinate(in Vector3 coordinate, in Matrix4x4 transform, out Vector3 result)
    {
        result.X = (coordinate.X * transform.M11) + (coordinate.Y * transform.M21) + (coordinate.Z * transform.M31) + transform.M41;
        result.Y = (coordinate.X * transform.M12) + (coordinate.Y * transform.M22) + (coordinate.Z * transform.M32) + transform.M42;
        result.Z = (coordinate.X * transform.M13) + (coordinate.Y * transform.M23) + (coordinate.Z * transform.M33) + transform.M43;
        var w = 1f / ((coordinate.X * transform.M14) + (coordinate.Y * transform.M24) + (coordinate.Z * transform.M34) + transform.M44);
        result *= w;
    }

    public static Vector2 WorldToScreenOld(in Matrix4x4 viewProj, in Vector3 worldPos)
    {
        TransformCoordinate(worldPos, viewProj, out Vector3 viewPos);
        return new Vector2(
            0.5f * ImGuiHelpers.MainViewport.Size.X * (1 + viewPos.X),
            0.5f * ImGuiHelpers.MainViewport.Size.Y * (1 - viewPos.Y)) + ImGuiHelpers.MainViewport.Pos;
    }

    public static Vector2 WorldToScreen(in Matrix4x4 viewProj, in Vector3 worldPos)
    {
        var viewPos = Vector4.Transform(worldPos, viewProj);
        return new Vector2(
            0.5f * ImGuiHelpers.MainViewport.Size.X * (1 + viewPos.X),
            0.5f * ImGuiHelpers.MainViewport.Size.Y * (1 - viewPos.Y)) + ImGuiHelpers.MainViewport.Pos;
    }
}
