﻿using ProtoBuf;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using ShipPoints;
using ShipPoints.HeartNetworking;

namespace SCModRepository_Dev.Gamemode_Mods.Development.Starcore_Sharetrack_Dev.Data.Scripts.ShipPoints.HeartNetworking.
    Custom
{
    [ProtoContract]
    internal class ShieldFillRequestPacket : PacketBase
    {
        public override void Received(ulong SenderSteamId)
        {
            foreach (var g in MyEntities.GetEntities())
                if (g != null && !g.MarkedForClose && g is MyCubeGrid)
                {
                    var grid = g as MyCubeGrid;
                    var block = PointCheck.I.ShieldApi.GetShieldBlock(grid);
                    if (block != null) PointCheck.I.ShieldApi.SetCharge(block, 99999999999);
                }

            MyAPIGateway.Utilities.ShowMessage("Shields", "Charged");
            if (MyAPIGateway.Session.IsServer)
                HeartNetwork.I.SendToEveryone(this);
        }
    }
}
