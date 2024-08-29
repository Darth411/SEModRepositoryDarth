using System.Collections.Generic;
using Sandbox.Game;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRageMath;
using Draygo.API;
using VRage.Game.Entity;
using System;
using System.Text;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;
using Sandbox.Game.Entities;
using VRage.Input;
using System.Linq;
using ProtoBuf;

namespace Klime.ProximityEntry
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class ProximityEntry : MySessionComponentBase
    {
        HudAPIv2 HUD_Base;
        HudAPIv2.HUDMessage HUD_Message;
        StringBuilder text = new StringBuilder("");

        IMyCharacter reuse_character;
        IHitInfo raycast;
        MatrixD camera_matrix;
        List<IMyCockpit> cockpits = new List<IMyCockpit>();

        MyCubeGrid reuse_cubegrid;
        List<IMyCubeGrid> reuse_gridgroup = new List<IMyCubeGrid>();
        IMyCockpit reuse_cockpit;

        ushort net_id = 49864;
        int timer = 0;
        int current_hold = 0;
        int max_hold = 30;

        private bool isHoldingF = false;
        private double holdStartTime = 0;
        private const double HOLD_DURATION = 0.5; // Half a second hold duration
        private const double COOLDOWN_DURATION = 1.0; // One second cooldown duration
        private double cooldownEndTime = 0;

        [ProtoContract]
        public class EntryRequest
        {
            [ProtoMember(1)]
            public long cockpit_id;

            [ProtoMember(2)]
            public long player_id;

            [ProtoMember(3)]
            public long character_id;
            public EntryRequest()
            {

            }

            public EntryRequest(long cockpit_id, long player_id, long character_id)
            {
                this.cockpit_id = cockpit_id;
                this.player_id = player_id;
                this.character_id = character_id;
            }
        }


        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            if (MyAPIGateway.Session.IsServer)
            {
                MyAPIGateway.Multiplayer.RegisterMessageHandler(net_id, CockpitRequestHandler);
            }
        }

        private void CockpitRequestHandler(byte[] obj)
        {
            EntryRequest request = MyAPIGateway.Utilities.SerializeFromBinary<EntryRequest>(obj);
            if (request != null)
            {                              
                reuse_cockpit = MyAPIGateway.Entities.GetEntityById(request.cockpit_id) as IMyCockpit;
                reuse_character = MyAPIGateway.Entities.GetEntityById(request.character_id) as IMyCharacter;
                if (reuse_cockpit != null && reuse_character != null && reuse_cockpit.Pilot == null)
                {
                    reuse_cockpit.AttachPilot(reuse_character);

                    // Add this line to show a notification
                    MyAPIGateway.Utilities.ShowNotification($"Entered cockpit: {reuse_cockpit.CustomName}", 1000 / 60, "White");
                }
            }
        }

        public override void BeforeStart()
        {
            HUD_Base = new HudAPIv2(HUD_Init_Complete);
        }

        private void HUD_Init_Complete()
        {
            HUD_Message = new HudAPIv2.HUDMessage(text, new Vector2D(-1, 0), null, -1, 1, true, false, null, BlendTypeEnum.PostPP, "white");
            HUD_Message.InitialColor = Color.Red;
            HUD_Message.Scale *= 1.2;
            HUD_Message.Origin = new Vector2D(-0.2, 0.8);
        }

        public override void Draw()
        {
            try
            {
                if (MyAPIGateway.Utilities.IsDedicated) return;

                if (timer % 10 == 0)
                {
                    UpdateRaycastInfo();
                }

                if (reuse_cubegrid != null && !reuse_cubegrid.MarkedForClose)
                {
                    if (MyAPIGateway.Input.IsKeyPress(MyKeys.F))
                    {
                        if (ValidInput() && MyAPIGateway.Session?.Player?.Character != null)
                        {
                            MyAPIGateway.CubeBuilder.DeactivateBlockCreation();
                            ShowTargetCockpit();
                        }
                    }
                    else if (MyAPIGateway.Input.IsNewKeyReleased(MyKeys.F))
                    {
                        if (ValidInput() && MyAPIGateway.Session?.Player?.Character != null)
                        {
                            AddToCockpit();
                        }
                    }
                    else
                    {
                        current_hold = 0;
                    }
                }
                else
                {
                    current_hold = 0;
                }

                UpdateHUDMessage();
            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowMessage("", e.Message);
            }
            timer += 1;
        }

        private void UpdateRaycastInfo()
        {
            reuse_cubegrid = null;
            if (MyAPIGateway.Session?.Player?.Character != null && MyAPIGateway.Session.Player?.Controller?.ControlledEntity is IMyCharacter
                                                                && MyAPIGateway.Session.CameraController != null && MyAPIGateway.Session.CameraController.IsInFirstPersonView && MyAPIGateway.Session.Camera != null)
            {
                reuse_character = MyAPIGateway.Session.Player.Character;
                camera_matrix = MyAPIGateway.Session.Camera.WorldMatrix;

                MyAPIGateway.Physics.CastRay(camera_matrix.Translation, camera_matrix.Translation + camera_matrix.Forward * 80, out raycast);

                if (raycast != null && raycast.HitEntity != null)
                {
                    reuse_cubegrid = raycast.HitEntity as MyCubeGrid;
                    if (reuse_cubegrid == null || reuse_cubegrid.Physics == null || reuse_cubegrid.IsStatic || reuse_cubegrid.GridSizeEnum == MyCubeSize.Small)
                    {
                        reuse_cubegrid = null;
                    }

                    if (reuse_cubegrid != null)
                    {
                        reuse_gridgroup = MyAPIGateway.GridGroups.GetGroup(reuse_cubegrid, GridLinkTypeEnum.Logical);
                        foreach (var grid in reuse_gridgroup)
                        {
                            if (grid.GridSizeEnum == MyCubeSize.Small)
                            {
                                reuse_cubegrid = null;
                                break;
                            }
                        }

                    }
                }
            }
        }

        private void ShowTargetCockpit()
        {
            IMyCockpit targetCockpit = GetTargetCockpit();
            if (targetCockpit != null)
            {
                string cockpitName = string.IsNullOrEmpty(targetCockpit.CustomName) ? targetCockpit.DefinitionDisplayNameText : targetCockpit.CustomName;
                MyAPIGateway.Utilities.ShowNotification($"Target cockpit: {cockpitName}", 1000 / 60, "White"); // Show for 1 second
            }
        }

        private void AddToCockpit()
        {
            try
            {
                IMyCockpit targetCockpit = GetTargetCockpit();
                if (targetCockpit != null)
                {
                    SendEntryRequest(targetCockpit);
                    DisplayCockpitName(targetCockpit, reuse_cubegrid.DisplayName);
                }
            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowMessage("", e.Message);
            }
        }

        private IMyCockpit GetTargetCockpit()
        {
            if (reuse_cubegrid?.Physics == null || reuse_cubegrid.IsStatic) return null;

            IMyCockpit mainCockpit = null;
            IMyCockpit closestCockpit = null;

            foreach (var fatblock in reuse_cubegrid.GetFatBlocks())
            {
                IMyCockpit cockpit = fatblock as IMyCockpit;
                if (cockpit != null && cockpit.Pilot == null)
                {
                    if (cockpit.IsMainCockpit)
                    {
                        mainCockpit = cockpit;
                    }
                    else if (IsClosestCockpit(cockpit, reuse_cubegrid))
                    {
                        closestCockpit = cockpit;
                    }
                }
            }

            return mainCockpit ?? closestCockpit;
        }

        private void UpdateHUDMessage()
        {
            if (HUD_Message != null)
            {
                if (reuse_cubegrid != null)
                {
                    text.Clear();
                    text.Append("Hold F to target cockpit, release to enter: " + reuse_cubegrid.DisplayName);
                    HUD_Message.Visible = true;
                }
                else
                {
                    HUD_Message.Visible = false;
                }
            }
        }

        private void DisplayCockpitName(IMyCockpit cockpit, string gridName)
        {
            text.Clear();
            string cockpitName = string.IsNullOrEmpty(cockpit.CustomName) ? cockpit.DefinitionDisplayNameText : cockpit.CustomName;
            MyAPIGateway.Utilities.ShowNotification($"Entering: {gridName} ({cockpitName})", 1000, "White"); // Show for 1 second
        }

        private bool IsClosestCockpit(IMyCockpit cockpit, MyCubeGrid cubeG)
        {
            var player_pos = MyAPIGateway.Session.Player.Character.WorldMatrix.Translation;
            var closestCockpit = cubeG.GetFatBlocks().OfType<IMyCockpit>()
                                   .OrderBy(o => Vector3D.Distance(o.WorldMatrix.Translation, player_pos))
                                   .FirstOrDefault();

            return cockpit == closestCockpit;
        }

        private void SendEntryRequest(IMyCockpit cockpit)
        {
            EntryRequest request = new EntryRequest(cockpit.EntityId, MyAPIGateway.Session.Player.IdentityId, MyAPIGateway.Session.Player.Character.EntityId);
            MyAPIGateway.Multiplayer.SendMessageToServer(net_id, MyAPIGateway.Utilities.SerializeToBinary<EntryRequest>(request));

            // Add this line to show a local notification
            MyAPIGateway.Utilities.ShowNotification($"Attempting to enter: {cockpit.CustomName}", 500, "White");
        }

        private bool ValidInput()
        {
            if (!MyAPIGateway.Gui.ChatEntryVisible && !MyAPIGateway.Gui.IsCursorVisible && MyAPIGateway.Gui.GetCurrentScreen == MyTerminalPageEnum.None &&
                !MyAPIGateway.Session.IsCameraUserControlledSpectator)
            {
                return true;
            }
            return false;
        }

        protected override void UnloadData()
        {
            if (HUD_Base != null)
            {
                HUD_Base.Unload();
            }
            if (MyAPIGateway.Session.IsServer)
            {
                MyAPIGateway.Multiplayer.UnregisterMessageHandler(net_id, CockpitRequestHandler);
            }
        }
    }
}