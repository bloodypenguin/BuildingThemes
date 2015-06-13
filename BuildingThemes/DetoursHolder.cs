﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Reflection;
using System.Threading;
using ColossalFramework;
using ColossalFramework.Math;
using UnityEngine;

namespace BuildingThemes
{

    class DetoursHolder
    {

        private static Dictionary<ulong, ushort> seedTable = new Dictionary<ulong, ushort>(); 

        public static void InitTable()
        {

            for (ushort _seed = 0; _seed <= 65534; ++_seed) { 
                var seed = (ulong) (6364136223846793005L*(long) _seed + 1442695040888963407L);
                seedTable.Add(seed, _seed);
            }
        }

        private static readonly object Lock = new object();

        //we'll use this variable to pass position to GetRandomBuildingInfo method. Or we can just pass District
        public class Position
        {
            private Vector3 m_position;

            private Position(Vector3 position)
            {
                this.m_position = position;
            }

            public Vector3 getValue()
            {
                return m_position;
            }

            public static Position Build(Vector3 position)
            {
                return new Position(position);
            }
        }

        public static Position position = null;

        public static RedirectCallsState zoneBlockSimulationStepState;


        //this is detoured version of BuildingManger#GetRandomBuildingInfo method. Note, that it's an instance method. It's better because this way all registers will be expected to have the same values
        //as in original methods
        public BuildingInfo GetRandomBuildingInfo(ref Randomizer r, ItemClass.Service service, ItemClass.SubService subService, ItemClass.Level level, int width, int length, BuildingInfo.ZoningMode zoningMode)
        {
            UnityEngine.Debug.LogFormat("Building Themes: Detoured GetRandomBuildingInfo was called. seed: {0} (singleton seed: {1}). service: {2}, subService: {3}," +
                "level: {4}, width: {5}, length: {6}, zoningMode: {7}, current thread: {8}\nStack trace: {9}", r.seed, Singleton<SimulationManager>.instance.m_randomizer.seed,
                service, subService, level, width, length, zoningMode,
                                        Thread.CurrentThread.ManagedThreadId, System.Environment.StackTrace);
            //this part is the same as in original method
            //I love this hack :) This is how I get this reference - and save it to a variable
            var buildingManager = (BuildingManager)Convert.ChangeType(this, typeof(BuildingManager));
            var refreshAreaBuidlings = typeof(BuildingManager).GetMethod("RefreshAreaBuildings", BindingFlags.NonPublic | BindingFlags.Instance);
            refreshAreaBuidlings.Invoke(buildingManager, new object[] { });
            var areaBuildings = (FastList<ushort>[])GetInstanceField(typeof(BuildingManager), buildingManager,
                "m_areaBuildings");
            var getAreaIndex = typeof(BuildingManager).GetMethod("GetAreaIndex", BindingFlags.NonPublic | BindingFlags.Static);

            FastList<ushort> fastList = areaBuildings[(int)getAreaIndex.Invoke(null, new object[] { service, subService, level, width, length, zoningMode })];
            if (fastList == null)
            {
                UnityEngine.Debug.LogFormat("Building Themes: Fast list is null. Return null, current thread: {0}",
                     Thread.CurrentThread.ManagedThreadId);
                return (BuildingInfo)null;
            }

            if (fastList.m_size == 0)
            {
                UnityEngine.Debug.LogFormat("Building Themes: Fast list is empty. Return null, current thread: {0}", Thread.CurrentThread.ManagedThreadId);
                return (BuildingInfo)null;
            }

            if (r.seed == Singleton<SimulationManager>.instance.m_randomizer.seed)
            {

                do
                {
                } while (!Monitor.TryEnter(Lock, SimulationManager.SYNCHRONIZE_TIMEOUT));
                try
                {
                    var positionStr = position != null ? position.getValue().ToString() : "null";
                    UnityEngine.Debug.LogFormat("Building Themes: Getting position from static variable. position: {0}, current thread: {1}",
                        positionStr, Thread.CurrentThread.ManagedThreadId);
                    if (position != null)
                    {
                        FilterList(position, ref fastList);
                        position = null;
                    }
                    else
                    {
                        UnityEngine.Debug.LogFormat(
                            "Building Themes: Detoured GetRandomBuildingInfo. No position was specified!;current thread: {0}",
                            Thread.CurrentThread.ManagedThreadId);

                    }
                }
                finally
                {
                    Monitor.Exit(Lock);
                }
            }
            else
            {
                UnityEngine.Debug.LogFormat(
                    "Building Themes: Getting position from seed {0}... current thread: {1}", r.seed, Thread.CurrentThread.ManagedThreadId);
                var buildingId = seedTable[r.seed];
                var building = Singleton<BuildingManager>.instance.m_buildings.m_buffer[buildingId];
                var buildingPosition = Position.Build(building.m_position);
                UnityEngine.Debug.LogFormat(
                        "Building Themes: Getting position from seed {0}. building: {1}, buildingId: {2}, position: {3}, threadId: {4}",
                        r.seed, building.Info.name, buildingId, buildingPosition.getValue(), 
                        Thread.CurrentThread.ManagedThreadId);
                FilterList(buildingPosition, ref fastList);
            }

            int index = r.Int32((uint)fastList.m_size);
            return PrefabCollection<BuildingInfo>.GetPrefab((uint)fastList.m_buffer[index]);
        }

        private static void FilterList(Position position, ref FastList<ushort> list)
        {
            ushort districtIdx = Singleton<DistrictManager>.instance.GetDistrict(position.getValue());
            //districtIdx==0 probably means 'outside of any district'
            UnityEngine.Debug.LogFormat("Building Themes: Detoured GetRandomBuildingInfo. districtIdx: {0};current thread: {1}",
                districtIdx, Thread.CurrentThread.ManagedThreadId);
            //District district = Singleton<DistrictManager>.instance.m_districts.m_buffer[districtIdx];
            //TODO(earalov): here fastList variable should be filtered. All buildings that  don't belong to the district should be removed from this list.
        }


        public void ZoneBlockSimulationStep(ushort blockID)
        {
            var zoneBlock = Singleton<ZoneManager>.instance.m_blocks.m_buffer[blockID];
            UnityEngine.Debug.LogFormat("Building Themes: Detoured ZoneBlock.SimulationStep was called. blockID: {0}, position: {1}. current thread: {2}", blockID, zoneBlock.m_position, Thread.CurrentThread.ManagedThreadId);
            position = Position.Build(zoneBlock.m_position);
            var methodInfo = typeof(ZoneBlock).GetMethod("SimulationStep", BindingFlags.Public | BindingFlags.Instance);
            RedirectionHelper.RevertRedirect(
                methodInfo,
                zoneBlockSimulationStepState
                );
            methodInfo.Invoke(zoneBlock, new object[] { blockID });
            RedirectionHelper.RedirectCalls(
                methodInfo,
                typeof(DetoursHolder).GetMethod("ZoneBlockSimulationStep", BindingFlags.Public | BindingFlags.Instance)
                );

        }

        internal static object GetInstanceField(Type type, object instance, string fieldName)
        {
            BindingFlags bindFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Static;
            FieldInfo field = type.GetField(fieldName, bindFlags);
            return field.GetValue(instance);
        }
    }
}
