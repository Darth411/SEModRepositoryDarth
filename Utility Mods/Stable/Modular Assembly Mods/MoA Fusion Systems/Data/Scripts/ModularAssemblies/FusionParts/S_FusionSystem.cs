﻿using System;
using System.Collections.Generic;
using MoA_Fusion_Systems.Data.Scripts.ModularAssemblies.Communication;
using MoA_Fusion_Systems.Data.Scripts.ModularAssemblies.FusionParts.FusionReactor;
using MoA_Fusion_Systems.Data.Scripts.ModularAssemblies.FusionParts.FusionThruster;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRage.Game.ModAPI;

namespace MoA_Fusion_Systems.Data.Scripts.ModularAssemblies.
    FusionParts
{
    internal class S_FusionSystem
    {
        public const float MegawattsPerFusionPower = 30;
        public const float NewtonsPerFusionPower = 3200000;

        public List<S_FusionArm> Arms = new List<S_FusionArm>();
        public int BlockCount = 0;
        public int PhysicalAssemblyId;

        /// <summary>
        /// Total power generated minus PowerConsumption
        /// </summary>
        public float PowerGeneration;
        /// <summary>
        /// Total power consumed
        /// </summary>
        public float PowerConsumption;
        /// <summary>
        /// Current power stored
        /// </summary>
        public float PowerStored;
        /// <summary>
        /// Maximum power storage
        /// </summary>
        public float MaxPowerStored;
        public List<FusionReactorLogic> Reactors = new List<FusionReactorLogic>();
        public List<FusionThrusterLogic> Thrusters = new List<FusionThrusterLogic>();

        public S_FusionSystem(int physicalAssemblyId)
        {
            PhysicalAssemblyId = physicalAssemblyId;
        }

        private static ModularDefinitionAPI ModularAPI => ModularDefinition.ModularAPI;

        public void AddPart(IMyCubeBlock newPart)
        {
            if (newPart == null)
                return;

            BlockCount++;

            // Scan for 'arms' connected on both ends to the feeder block.
            switch (newPart.BlockDefinition.SubtypeName)
            {
                case "Caster_Accelerator_0":
                case "Caster_Accelerator_90":
                    var newArm = new S_FusionArm((MyEntity)newPart, "Caster_Feeder");
                    if (newArm.IsValid)
                    {
                        Arms.Add(newArm);
                        UpdatePower(true);
                    }

                    break;
            }

            if (newPart is IMyThrust)
            {
                var logic = newPart.GameLogic.GetAs<FusionThrusterLogic>();
                if (logic != null)
                {
                    Thrusters.Add(logic);
                    logic.MemberSystem = this;
                    logic.UpdateThrust(PowerGeneration, NewtonsPerFusionPower);
                }
            }

            if (newPart is IMyReactor)
            {
                var logic = newPart.GameLogic.GetAs<FusionReactorLogic>();
                if (logic != null)
                {
                    Reactors.Add(logic);
                    logic.MemberSystem = this;
                    logic.UpdatePower(PowerGeneration, MegawattsPerFusionPower);
                }
            }

            UpdatePower();
        }

        public void RemovePart(IMyCubeBlock part)
        {
            if (part == null)
                return;

            BlockCount--;

            if (part is IMyThrust)
            {
                var logic = part.GameLogic.GetAs<FusionThrusterLogic>();
                logic.MemberSystem = null;
                Thrusters.Remove(logic);
            }

            if (part is IMyReactor)
            {
                var logic = part.GameLogic.GetAs<FusionReactorLogic>();
                logic.MemberSystem = null;
                Reactors.Remove(logic);
            }

            foreach (var arm in Arms)
                if (arm.Parts.Contains(part))
                {
                    Arms.Remove(arm);
                    UpdatePower(true);
                    break;
                }

            if (BlockCount <= 0)
                S_FusionManager.I.FusionSystems.Remove(PhysicalAssemblyId);
        }

        private void UpdatePower(bool updateReactors = false)
        {
            var powerGeneration = 0.01f;
            var powerCapacity = 0.01f;
            var totalPowerUsage = 0f;

            foreach (var arm in Arms)
            {
                powerGeneration += arm.PowerGeneration;
                powerCapacity += arm.PowerStorage;
            }

            // Math for slider on reactor parts to allow for a power <-> efficiency tradeoff.
            foreach (var reactor in Reactors)
            {
                totalPowerUsage += reactor?.PowerConsumption ?? 0;

                if (updateReactors)
                    reactor?.UpdatePower(powerGeneration, MegawattsPerFusionPower);
            }

            foreach (var thruster in Thrusters)
            {
                totalPowerUsage += thruster?.PowerConsumption ?? 0;

                if (updateReactors)
                    thruster?.UpdateThrust(powerGeneration, NewtonsPerFusionPower);
            }

            // Subtract power usage afterwards so that all reactors have the same stats.
            PowerGeneration = powerGeneration;
            MaxPowerStored = powerCapacity;
            PowerConsumption = totalPowerUsage;
            PowerGeneration -= totalPowerUsage;

            // Update PowerStored
            PowerStored += PowerGeneration;
            if (PowerStored > MaxPowerStored)
                PowerStored = MaxPowerStored;
        }

        public void UpdateTick()
        {
            UpdatePower();
        }
    }
}