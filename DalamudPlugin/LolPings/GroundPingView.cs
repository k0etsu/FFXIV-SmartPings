using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ImGuiNET;
using LolPings.Extensions;
using LolPings.Input;
using LolPings.Log;
using LolPings.UI.Util;
using LolPings.UI.View;
using Pictomancy;
using System;
using System.Numerics;
using System.Reactive.Subjects;

namespace LolPings;

public unsafe class GroundPingView : IPluginUIView
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
    private readonly ITextureProvider textureProvider;
    private readonly IKeyState keyState;
    private readonly InputEventSource inputEventSource;
    private readonly MapManager mapManager;
    private readonly ILogger logger;

    private bool leftMouseUpThisFrame;
    private bool createPingOnLeftMouseUp;

    public GroundPingView(
        Lazy<GroundPingPresenter> presenter,
        IDalamudPluginInterface pluginInterface,
        IClientState clientState,
        ITextureProvider textureProvider,
        IKeyState keyState,
        InputEventSource inputEventSource,
        MapManager mapManager,
        ILogger logger)
    {
        this.presenter = presenter;
        this.pluginInterface = pluginInterface;
        this.clientState = clientState;
        this.textureProvider = textureProvider;
        this.keyState = keyState;
        this.inputEventSource = inputEventSource;
        this.mapManager = mapManager;
        this.logger = logger;

        this.inputEventSource.SubscribeToKeyDown(args =>
        {
            if (args.Key == WindowsInput.Events.KeyCode.LButton &&
                this.keyState.GetRawValue(Dalamud.Game.ClientState.Keys.VirtualKey.CONTROL) > 0)
            {
                createPingOnLeftMouseUp = true;
                // Both lines are needed to consistently capture mouse input
                ImGui.GetIO().WantCaptureMouse = true;
                ImGui.SetNextFrameWantCaptureMouse(true);
            }
        });
        this.inputEventSource.SubscribeToKeyUp(args =>
        {
            if (args.Key == WindowsInput.Events.KeyCode.LButton)
            {
                leftMouseUpThisFrame = true;
            }
        });
    }

    public void Draw()
    {
        if (this.presenter.Value == null) { return; }

        if (createPingOnLeftMouseUp)
        {
            ImGui.SetNextFrameWantCaptureMouse(true);
        }

        if (clientState.LocalPlayer != null)
        {
            if (this.keyState.GetRawValue(Dalamud.Game.ClientState.Keys.VirtualKey.CONTROL) > 0)
            {
                if (createPingOnLeftMouseUp && leftMouseUpThisFrame)
                {
                    if (PictoService.GameGui.ScreenToWorld(ImGui.GetMousePos(), out var worldPos))
                    {
                        this.logger.Debug("Clicked on world position {0} for ground ping", worldPos);
                        var ping = new GroundPing
                        {
                            PingType = GroundPing.Type.Question,
                            StartTimestamp = DateTime.UtcNow.Ticks,
                            Author = this.clientState.LocalPlayer.Name.TextValue,
                            MapId = this.mapManager.GetCurrentMapPublicRoomName(),
                            WorldPosition = worldPos,
                        };
                        this.addGroundPing.OnNext(ping);
                    }
                    createPingOnLeftMouseUp = false;
                }
            }
            else
            {
                createPingOnLeftMouseUp = false;
            }

            var mapId = this.mapManager.GetCurrentMapPublicRoomName();
            for (var i = this.presenter.Value.GroundPings.Count - 1; i >= 0; i--)
            {
                var p = this.presenter.Value.GroundPings[i];

                if (mapId != p.MapId)
                {
                    this.presenter.Value.GroundPings.RemoveAt(i);
                    continue;
                }

                if (PictoService.GameGui.WorldToScreen(p.WorldPosition, out var screenPos))
                {
                    //this.logger.Info("Processing ping {0}. ScreenPos {1}", i, screenPos);
                    if (!DrawQuestionMarkPing(ImGui.GetForegroundDrawList(), screenPos, new(250, 250), p.DrawDuration, p.Author))
                    {
                        this.presenter.Value.GroundPings.RemoveAt(i);
                        continue;
                    }
                }
                p.DrawDuration += ImGui.GetIO().DeltaTime;

                // Extra cleanup
                if (p.DrawDuration > 10)
                {
                    this.presenter.Value.GroundPings.RemoveAt(i);
                    continue;
                }
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

    private bool DrawQuestionMarkPing(ImDrawListPtr drawList, Vector2 position, Vector2 size, float time, string? author)
    {
        var img = this.textureProvider.GetFromFile(this.pluginInterface.GetResourcePath("question_sheet.png")).GetWrapOrDefault()?.ImGuiHandle ?? default;
        float fps = 45;
        int rowCount = 5;
        int colCount = 11;
        int totalFrames = 54;
        int totalWidth = 7700;
        int totalHeight = 3500;

        int frame = (int)(time * fps); // 0-index
        if (frame >= totalFrames)
        {
            return false;
        }

        int width = totalWidth / colCount;
        int height = totalHeight / rowCount;

        int row = frame / colCount;
        int col = frame % colCount;

        var uv0 = new Vector2((float)col / colCount, (float)row / rowCount);
        var uv1 = uv0 + new Vector2((float)width / totalWidth, (float)height / totalHeight);

        drawList.AddImage(img, position - size / 2, position + size / 2, uv0, uv1);

        var fontSize = 20;
        var textSize = ImGui.CalcTextSize(author) * fontSize / 16;
        var minPad = new Vector2(-5, 0);
        drawList.AddRectFilled(position + minPad, position + textSize, Vector4Colors.Black.WithAlpha(0.6f).ToColorU32());
        drawList.AddText(ImGui.GetFont(), fontSize, position, Vector4Colors.White.ToColorU32(), author);

        return true;
    }
}
