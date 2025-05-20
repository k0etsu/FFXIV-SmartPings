﻿using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using SmartPings.Extensions;
using SmartPings.Input;
using SmartPings.Log;
using SmartPings.UI.Util;
using SmartPings.UI.View;
using System;
using System.Numerics;
using System.Reactive.Subjects;

namespace SmartPings;

public class GroundPingView : IPluginUIView, IDisposable
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
    private readonly IAddonLifecycle addonLifecycle;
    private readonly IAddonEventManager addonEventManager;
    private readonly IDataManager dataManager;
    private readonly InputEventSource inputEventSource;
    private readonly KeyStateWrapper keyStateWrapper;
    private readonly Configuration configuration;
    private readonly MapManager mapManager;
    private readonly ILogger logger;

    private enum PingWheelSection
    {
        Center,
        Left,
        Up,
        Right,
        Down,
    }

    private const float LEFT_CLICK_HOLD_DURATION_FOR_PING_WHEEL = 0.4f;
    private const float PING_WHEEL_SIZE = 310;
    private const float PING_WHEEL_CENTER_SIZE_MULTIPLIER = 0.141f;

    private bool IsAnyPingEnabled => this.configuration.EnableGroundPings || this.configuration.EnableGuiPings;
    private bool IsQuickPingKeybindDown => this.keyStateWrapper.IsVirtualKeyValid(this.configuration.QuickPingKeybind) &&
        this.keyStateWrapper.GetRawValue(this.configuration.QuickPingKeybind) > 0;
    private bool CreatePingOnLeftMouseUp => pingLeftClickPosition.HasValue;

    private bool cursorIsPing;
    private bool leftMouseUpThisFrame;
    private Vector2? pingLeftClickPosition;
    private float pingLeftClickHeldDuration;
    private bool pingWheelActive;
    private PingWheelSection activePingWheelSection;

    private Matrix4x4 viewProj;
    private Vector4 nearPlane;

    public GroundPingView(
        Lazy<GroundPingPresenter> presenter,
        IDalamudPluginInterface pluginInterface,
        IClientState clientState,
        IGameGui gameGui,
        ITextureProvider textureProvider,
        IAddonLifecycle addonLifecycle,
        IAddonEventManager addonEventManager,
        IDataManager dataManager,
        InputEventSource inputEventSource,
        KeyStateWrapper keyStateWrapper,
        Configuration configuration,
        MapManager mapManager,
        GuiPingHandler uiPingHandler,
        ILogger logger)
    {
        this.presenter = presenter;
        this.pluginInterface = pluginInterface;
        this.clientState = clientState;
        this.gameGui = gameGui;
        this.textureProvider = textureProvider;
        this.addonLifecycle = addonLifecycle;
        this.addonEventManager = addonEventManager;
        this.dataManager = dataManager;
        this.inputEventSource = inputEventSource;
        this.keyStateWrapper = keyStateWrapper;
        this.configuration = configuration;
        this.mapManager = mapManager;
        this.logger = logger;

        this.keyStateWrapper.OnKeyDown += key =>
        {
            if (key == this.configuration.PingKeybind && this.IsAnyPingEnabled)
            {
                cursorIsPing = true;
            }
            else if (key == Dalamud.Game.ClientState.Keys.VirtualKey.ESCAPE)
            {
                cursorIsPing = false;
            }
        };

        this.inputEventSource.SubscribeToKeyDown(args =>
        {
            if (this.IsAnyPingEnabled &&
                args.Key == WindowsInput.Events.KeyCode.LButton && (IsQuickPingKeybindDown || cursorIsPing))
            {
                if (ImGui.IsWindowHovered(ImGuiHoveredFlags.AnyWindow))
                {
                    return;
                }

                if (uiPingHandler.TryPingUi())
                {
                    return;
                }

                if (!this.configuration.EnableGroundPings) { return; }

                ImGuiExtensions.CaptureMouseThisFrame();
                cursorIsPing = false;
                pingLeftClickPosition = ImGui.GetMousePos();
                pingLeftClickHeldDuration = 0;
                pingWheelActive = false;
            }

            if (args.Key == WindowsInput.Events.KeyCode.RButton)
            {
                cursorIsPing = false;
            }
        });

        this.inputEventSource.SubscribeToKeyUp(args =>
        {
            if (args.Key == WindowsInput.Events.KeyCode.LButton)
            {
                cursorIsPing = false;
                leftMouseUpThisFrame = true;
            }
        });

        this.keyStateWrapper.OnKeyUp += key =>
        {
            // this.logger.Debug(this.configuration.QuickerPingKeybind + " " + key.ToString());
            if (key == this.configuration.QuickerPingKeybind && IsAnyPingEnabled)
            {
                if (ImGui.IsWindowHovered(ImGuiHoveredFlags.AnyWindow))
                {
                    return;
                }

                if (uiPingHandler.TryPingUi())
                {
                    return;
                }

                if (!this.configuration.EnableGroundPings) { return; }

                ImGuiExtensions.CaptureMouseThisFrame();
                pingLeftClickPosition = ImGui.GetMousePos();
                pingLeftClickHeldDuration = 0;
                pingWheelActive = false;

                cursorIsPing = false;
                leftMouseUpThisFrame = true;
            }
        };
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    public void Draw()
    {
        if (this.presenter.Value == null) { return; }

        if (CreatePingOnLeftMouseUp)
        {
            ImGui.SetNextFrameWantCaptureMouse(true);
        }

        if (clientState.LocalPlayer != null)
        {
            if (!DrawPings())
            {
                this.presenter.Value.GroundPings.Clear();
            }

            if (CreatePingOnLeftMouseUp)
            {
                if (!leftMouseUpThisFrame)
                {
                    if (!pingWheelActive && this.configuration.EnablePingWheel)
                    {
                        pingLeftClickHeldDuration += ImGui.GetIO().DeltaTime;
                        var moveDistance = Vector2.Distance(ImGui.GetMousePos(), pingLeftClickPosition!.Value);
                        if (pingLeftClickHeldDuration > LEFT_CLICK_HOLD_DURATION_FOR_PING_WHEEL ||
                            moveDistance > PING_WHEEL_CENTER_SIZE_MULTIPLIER * PING_WHEEL_SIZE)
                        {
                            pingWheelActive = true;
                        }
                    }

                    if (pingWheelActive)
                    {
                        activePingWheelSection = DrawPingWheel(ImGui.GetForegroundDrawList(), pingLeftClickPosition!.Value, ImGui.GetMousePos());
                    }
                }
                else
                {
                    var pingType = this.configuration.DefaultGroundPingType;
                    if (pingWheelActive)
                    {
                        switch (activePingWheelSection)
                        {
                            case PingWheelSection.Center: pingType = GroundPing.Type.None; break;
                            case PingWheelSection.Left: pingType = GroundPing.Type.Question; break;
                            case PingWheelSection.Up: pingType = GroundPing.Type.Basic; break;
                            case PingWheelSection.Right: pingType = GroundPing.Type.Basic; break;
                            case PingWheelSection.Down: pingType = GroundPing.Type.Basic; break;
                        }
                    }

                    // When ping wheel isn't enabled, it feels better to place the ping at the mouse position on left click release rather than press
                    var pingPosition = this.configuration.EnablePingWheel ? pingLeftClickPosition!.Value : ImGui.GetMousePos();
                    if (pingType != GroundPing.Type.None &&
                        this.gameGui.ScreenToWorld(pingPosition, out var worldPos))
                    {
                        this.logger.Debug("Clicked on world position {0} for ground ping", worldPos);
                        var ping = new GroundPing
                        {
                            PingType = pingType,
                            StartTimestamp = DateTime.UtcNow.Ticks,
                            Author = this.clientState.LocalPlayer.Name.TextValue,
                            MapId = this.mapManager.GetCurrentMapPublicRoomName(),
                            WorldPosition = worldPos,
                        };
                        this.addGroundPing.OnNext(ping);
                    }

                    pingLeftClickPosition = null;
                    pingLeftClickHeldDuration = 0;
                    pingWheelActive = false;
                }
            }
        }
        else
        {
            this.presenter.Value.GroundPings.Clear();
        }

        unsafe
        {
            if (cursorIsPing)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.None);
                AtkStage.Instance()->AtkCursor.Hide();
                DrawPingCursor(ImGui.GetForegroundDrawList(), ImGui.GetMousePos(), 50 * Vector2.One);
            }
            else if (this.IsAnyPingEnabled && IsQuickPingKeybindDown)
            {
                var position = ImGui.GetMousePos() + new Vector2(14, 30);
                DrawPingCursor(ImGui.GetForegroundDrawList(), position, 25 * Vector2.One);
            }
        }

        leftMouseUpThisFrame = false;
    }

    private unsafe bool DrawPings()
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
                    pingDrawn = DrawBasicPing(ImGui.GetForegroundDrawList(), p.WorldPosition, p.DrawDuration, p.Author);
                    break;
                case GroundPing.Type.Question:
                    pingDrawn = DrawQuestionMarkPing(ImGui.GetForegroundDrawList(), p.WorldPosition, p.DrawDuration, p.Author);
                    break;
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

    #region Draw Functions

    private void DrawPingCursor(ImDrawListPtr drawList, Vector2 mousePosition, Vector2 size)
    {
        var img = this.textureProvider.GetFromFile(this.pluginInterface.GetResourcePath("ping_cursor.png")).GetWrapOrDefault()?.ImGuiHandle ?? default;
        drawList.AddImage(img, mousePosition - size / 2, mousePosition + size / 2);
    }

    private PingWheelSection DrawPingWheel(ImDrawListPtr drawList, Vector2 wheelPosition, Vector2 mousePosition)
    {
        var img = this.textureProvider.GetFromFile(this.pluginInterface.GetResourcePath("ping_wheel_sheet.png")).GetWrapOrDefault()?.ImGuiHandle ?? default;
        int rowCount = 2;
        int colCount = 3;
        int totalWidth = 1536;
        int totalHeight = 1024;

        var size = PING_WHEEL_SIZE;
        int frame;
        if (Vector2.Distance(wheelPosition, mousePosition) < PING_WHEEL_CENTER_SIZE_MULTIPLIER * size)
        {
            frame = 0;
        }
        else
        {
            var wheelToMouse = mousePosition - wheelPosition;
            var angle = MathF.Atan2(wheelToMouse.Y, wheelToMouse.X);
            if (angle >= -MathF.PI / 4 && angle <= MathF.PI / 4)
            {
                frame = 3; // right
            }
            else if (angle >= MathF.PI / 4 && angle <= MathF.PI * 3 / 4)
            {
                frame = 1; // down
            }
            else if (angle >= -MathF.PI * 3 / 4 && angle <= -MathF.PI / 4)
            {
                frame = 4; // up
            }
            else
            {
                frame = 2; // left
            }
        }

        GetFrameUVs(rowCount, colCount, totalWidth, totalHeight, frame, out var uv0, out var uv1);

        drawList.AddImage(img, wheelPosition - Vector2.One * size / 2, wheelPosition + Vector2.One * size / 2, uv0, uv1);

        if (frame > 0)
        {
            var direction = Vector2.Normalize(mousePosition - wheelPosition);
            var p1 = wheelPosition + PING_WHEEL_CENTER_SIZE_MULTIPLIER * size * direction;
            drawList.AddLine(p1, mousePosition, Vector4Colors.NeonBlue.ToColorU32(), 2);
        }

        return frame switch
        {
            1 => PingWheelSection.Down,
            2 => PingWheelSection.Left,
            3 => PingWheelSection.Right,
            4 => PingWheelSection.Up,
            _ => PingWheelSection.Center,
        };
    }

    private bool DrawBasicPing(ImDrawListPtr drawList, Vector3 worldPosition, float time, string? author)
    {
        return DrawPing(drawList, worldPosition, scale: 1.0f, time, author,
            minRingSize: 70, maxRingSize: 800,
            ringPath: "basic_ping_ring_sheet.png", ringRowCount: 5, ringColCount: 7, ringTotalFrames: 35, ringTotalWidth: 1792, ringTotalHeight: 1280,
            pingPath: "basic_ping_sheet.png",      pingRowCount: 4, pingColCount: 4, pingTotalFrames: 60, pingTotalWidth: 2048, pingTotalHeight: 2048,
            pingLastFrameOfAuthorTag: 46, pingFrame0HoldFrames: 46, pingToRingSizeRatio: 0.5f, pingPivotOffset: Vector2.Zero);
    }

    private bool DrawQuestionMarkPing(ImDrawListPtr drawList, Vector3 worldPosition, float time, string? author)
    {
        return DrawPing(drawList, worldPosition, scale: 0.7f, time, author,
            minRingSize: 70, maxRingSize: 800,
            ringPath: "question_ping_ring_sheet.png", ringRowCount: 5, ringColCount: 8, ringTotalFrames: 39, ringTotalWidth: 2048, ringTotalHeight: 1280,
            pingPath: "question_ping_sheet.png",      pingRowCount: 8, pingColCount: 8, pingTotalFrames: 58, pingTotalWidth: 2048, pingTotalHeight: 2048,
            pingLastFrameOfAuthorTag: 41, pingFrame0HoldFrames: 1, pingToRingSizeRatio: 1.1f, pingPivotOffset: new Vector2(0, 0.05f));
    }

    private void GetFrameUVs(int rowCount, int colCount, int totalWidth, int totalHeight, int frame, out Vector2 uv0, out Vector2 uv1)
    {
        int width = totalWidth / colCount;
        int height = totalHeight / rowCount;

        int row = frame / colCount;
        int col = frame % colCount;

        uv0 = new Vector2((float)col / colCount, (float)row / rowCount);
        uv1 = uv0 + new Vector2((float)width / totalWidth, (float)height / totalHeight);
    }

    private bool DrawPing(ImDrawListPtr drawList, Vector3 worldPosition, float scale, float time, string? author,
        int minRingSize, int maxRingSize,
        string ringPath, int ringRowCount, int ringColCount, int ringTotalFrames, int ringTotalWidth, int ringTotalHeight,
        string pingPath, int pingRowCount, int pingColCount, int pingTotalFrames, int pingTotalWidth, int pingTotalHeight,
        int pingLastFrameOfAuthorTag, int pingFrame0HoldFrames, float pingToRingSizeRatio, Vector2 pingPivotOffset)
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
        if (ringSize.X < minRingSize)
        {
            ringSize.Y *= minRingSize / ringSize.X;
            ringSize.X = minRingSize;
        }
        if (ringSize.X > maxRingSize)
        {
            ringSize.Y *= maxRingSize / ringSize.X;
            ringSize.X = maxRingSize;
        }

        float fps = 30;
        {
            var imgRing = this.textureProvider.GetFromFile(this.pluginInterface.GetResourcePath(ringPath)).GetWrapOrDefault()?.ImGuiHandle ?? default;

            int frame = (int)(time * fps);
            if (frame < ringTotalFrames)
            {
                if (onScreen)
                {
                    GetFrameUVs(ringRowCount, ringColCount, ringTotalWidth, ringTotalHeight, frame, out var uv0, out var uv1);

                    var p0 = new Vector2(screenPosition.X - ringSize.X / 2, screenPosition.Y - ringSize.Y / 2);
                    var p1 = new Vector2(screenPosition.X + ringSize.X / 2, screenPosition.Y + ringSize.Y / 2);

                    drawList.AddImage(imgRing, p0, p1, uv0, uv1);
                }
                draw = true;
            }
        }

        {
            var imgPing = this.textureProvider.GetFromFile(this.pluginInterface.GetResourcePath(pingPath)).GetWrapOrDefault()?.ImGuiHandle ?? default;

            int frame = (int)(time * fps);
            if (frame < pingTotalFrames)
            {
                if (onScreen)
                {
                    bool drawAuthorTag = frame <= pingLastFrameOfAuthorTag;

                    if (frame < pingFrame0HoldFrames)
                    {
                        frame = 0;
                    }
                    else
                    {
                        frame -= pingFrame0HoldFrames - 1;
                    }

                    GetFrameUVs(pingRowCount, pingColCount, pingTotalWidth, pingTotalHeight, frame, out var uv0, out var uv1);

                    var basicPingSize = ringSize.X * pingToRingSizeRatio;
                    var p0 = new Vector2(screenPosition.X - basicPingSize / 2, screenPosition.Y - basicPingSize);
                    var p1 = new Vector2(screenPosition.X + basicPingSize / 2, screenPosition.Y);
                    p0 += pingPivotOffset * basicPingSize;
                    p1 += pingPivotOffset * basicPingSize;
                    drawList.AddImage(imgPing, p0, p1, uv0, uv1);

                    if (drawAuthorTag)
                    {
                        // TODO: Factor in Dalamud global font scale
                        //var fontSize = 20;
                        var fontSize = Math.Clamp(0.125f * ringSize.X, 17, 30);
                        var textSize = ImGui.CalcTextSize(author) * fontSize / 16;
                        var minPad = new Vector2(-5, 0);
                        drawList.AddRectFilled(screenPosition + minPad, screenPosition + textSize, Vector4Colors.Black.WithAlpha(0.6f).ToColorU32());
                        drawList.AddText(ImGui.GetFont(), fontSize, screenPosition, Vector4Colors.White.ToColorU32(), author);
                    }
                }
                draw = true;
            }
        }

        return draw;
    }

    #endregion
}
