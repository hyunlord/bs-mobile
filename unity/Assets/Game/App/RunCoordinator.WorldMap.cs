using System.Collections;
using System.Linq;
using UnityEngine;

namespace Game.App
{
    public sealed partial class RunCoordinator
    {
#if UNITY_EDITOR
        public BarracksServerClient WorldCaptureTransport => barracksServer;
#endif
        bool worldCredentialsSubscribed, worldGatherConfirm;
        string worldHero, worldUnit, worldRecall;
        int worldTroops = 1;
        int worldViewX, worldViewY, worldSelectedX = -1, worldSelectedY = -1;
        void ShowPortraitWorldMap()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var endpoint = BarracksServerEndpoint();
            if (endpoint != null)
            {
                if (barracksServer == null) barracksServer = new BarracksServerClient(endpoint);
                if (UsesDeviceAccount)
                {
                    InitializeAccountAccess();
                    if (!worldCredentialsSubscribed) { AccountCredentialsChanged += ClearWorldCredentials; worldCredentialsSubscribed = true; }
                    if (!barracksServer.MatchesCredentials(AccountAccessToken, AuthenticatedAccountId)) barracksServer.ClearAuthentication();
                }
                var owned = barracksServer.RecruitView?.heroes;
                if (owned != null && (worldHero == null || !owned.ContainsKey(worldHero))) worldHero = owned.Keys.FirstOrDefault();
                var ready = barracksServer.Latest?.Training?.units;
                if (ready != null && !ready.Any(u => u.unitId == worldUnit && u.ready > 0)) worldUnit = ready.FirstOrDefault(u=>u.ready>0)?.unitId;
                var unit = ready?.FirstOrDefault(u=>u.unitId == worldUnit);
                if (unit != null) worldTroops = Mathf.Clamp(worldTroops, 1, Mathf.Max(1,unit.ready));
                MetaPage(UiScreen.WorldMap, () => PortraitMetaPanels.WorldMap(Ui, Progression.State, barracksServer, worldViewX, worldViewY,
                    worldSelectedX, worldSelectedY, () => StartCoroutine(WorldOperation(LoadWorldHome(false))),
                    () => StartCoroutine(WorldOperation(LoadWorldHome(true))),
                    (dx, dy) => StartCoroutine(WorldOperation(MoveWorldView(dx, dy))),
                    (x, y) => { worldSelectedX = x; worldSelectedY = y; worldGatherConfirm=false; ShowPortraitWorldMap(); },
                    UsesDeviceAccount ? (System.Action)(() =>
                    {
                        if (string.IsNullOrEmpty(AccountAccessToken)) ShowAccountSettings();
                        else StartCoroutine(WorldOperation(LoadWorldHome(false)));
                    }) : ShowBarracksConnection, NavigatePortraitMeta, new WorldMarchActions
                    {
                        Hero = worldHero, Unit = worldUnit, Count = worldTroops, Confirm = worldGatherConfirm, Recall = worldRecall,
                        SelectHero = value => { worldHero=value; worldGatherConfirm=false; ShowPortraitWorldMap(); },
                        SelectUnit = value => { worldUnit=value; worldGatherConfirm=false; ShowPortraitWorldMap(); },
                        ChangeCount = delta => { if (unit != null) worldTroops=Mathf.Clamp(worldTroops+delta,1,unit.ready); worldGatherConfirm=false; ShowPortraitWorldMap(); },
                        Gather = () =>
                        {
                            if (!worldGatherConfirm) { worldGatherConfirm=true; ShowPortraitWorldMap(); return; }
                            worldGatherConfirm=false;
                            var target=barracksServer.WorldChunk?.Target(worldSelectedX,worldSelectedY);
                            StartCoroutine(WorldOperation(barracksServer.DispatchGather(target,worldHero,new[] {new WorldMarchTroops {unitId=worldUnit,count=worldTroops}})));
                        },
                        RecallMarch = id =>
                        {
                            if(worldRecall != id){worldRecall=id;ShowPortraitWorldMap();return;}
                            worldRecall=null;StartCoroutine(WorldOperation(barracksServer.RecallWorldMarch(id)));
                        },
                        Retry = () => StartCoroutine(WorldOperation(barracksServer.RetryWorldMarch())),
                        Refresh = () => StartCoroutine(WorldOperation(RefreshWorldMarches())),
                        Shield = () => StartCoroutine(WorldOperation(barracksServer.LoadWorldShield())),
                        Training = ShowBarracksConnection
                    }));
                return;
            }
#endif
            ShowPortraitUnconnectedWorld(PortraitMetaPage.WorldMap);
        }
        void ClearWorldCredentials()
        {
            barracksServer?.ClearAuthentication(); worldSelectedX = worldSelectedY = -1; worldGatherConfirm=false; worldRecall=null; worldHero=null; worldUnit=null;
            if (screen == UiScreen.WorldMap) ShowPortraitWorldMap();
        }
        IEnumerator WorldOperation(IEnumerator operation)
        {
            var running = StartCoroutine(operation);
            if (screen == UiScreen.WorldMap) ShowPortraitWorldMap();
            yield return running;
            if (screen == UiScreen.WorldMap) ShowPortraitWorldMap();
        }
        IEnumerator RefreshWorldMarches()
        {
            if (barracksServer.Busy) yield break;
            worldGatherConfirm=false; worldRecall=null;
            yield return barracksServer.LoadWorldMarches();
            if (!barracksServer.Connected || barracksServer.Status != "connected") yield break;
            var chunk=barracksServer.WorldChunk;
            if (chunk != null) yield return barracksServer.LoadWorldChunk(chunk.X,chunk.Y);
            if (!barracksServer.Connected || barracksServer.Status != "connected") yield break;
            yield return barracksServer.Refresh();
        }
        IEnumerator LoadWorldHome(bool assign)
        {
            if (barracksServer.Busy) yield break;
            if (UsesDeviceAccount && !barracksServer.MatchesCredentials(AccountAccessToken, AuthenticatedAccountId))
            {
                if (string.IsNullOrEmpty(AccountAccessToken)) { ShowAccountSettings(); yield break; }
                yield return barracksServer.ConnectAuthenticated(AccountAccessToken, AuthenticatedAccountId);
            }
            if (!barracksServer.Connected) yield break;
            yield return barracksServer.LoadWorldCity(assign);
            if (barracksServer.WorldCity == null || barracksServer.Status != "connected") yield break;
            if (barracksServer.WorldChunk == null) yield return barracksServer.LoadWorldChunk(0, 0);
            if (barracksServer.WorldChunk == null || barracksServer.Status != "connected") yield break;
            var city = barracksServer.WorldCity; var size = barracksServer.WorldChunk.Size;
            yield return barracksServer.LoadWorldChunk(city.X / size, city.Y / size);
            if (barracksServer.Status != "connected") yield break;
            worldViewX = Mathf.Clamp(city.X - PortraitMetaPanels.WorldVisibleSide / 2, city.X / size * size, city.X / size * size + Mathf.Max(0, size - PortraitMetaPanels.WorldVisibleSide));
            worldViewY = Mathf.Clamp(city.Y - PortraitMetaPanels.WorldVisibleSide / 2, city.Y / size * size, city.Y / size * size + Mathf.Max(0, size - PortraitMetaPanels.WorldVisibleSide));
            worldSelectedX = city.X; worldSelectedY = city.Y;
            yield return barracksServer.LoadWorldMarches();
            if (barracksServer.Connected && barracksServer.Status == "connected") yield return barracksServer.Refresh();
        }
        IEnumerator MoveWorldView(int dx, int dy)
        {
            var chunk = barracksServer.WorldChunk;
            if (chunk == null || barracksServer.Busy) yield break;
            var side = Mathf.Min(PortraitMetaPanels.WorldVisibleSide, chunk.Size);
            var x = WorldMapResponse.MoveOrigin(worldViewX, side, dx); var y = WorldMapResponse.MoveOrigin(worldViewY, side, dy);
            var nextX = x / chunk.Size; var nextY = y / chunk.Size;
            if (nextX != chunk.X || nextY != chunk.Y)
            {
                yield return barracksServer.LoadWorldChunk(nextX, nextY);
                if (barracksServer.WorldChunk?.X != nextX || barracksServer.WorldChunk?.Y != nextY || barracksServer.Status != "connected") yield break;
                chunk = barracksServer.WorldChunk;
            }
            worldViewX = Mathf.Clamp(x, chunk.X * chunk.Size, (chunk.X + 1) * chunk.Size - side);
            worldViewY = Mathf.Clamp(y, chunk.Y * chunk.Size, (chunk.Y + 1) * chunk.Size - side);
            worldSelectedX = worldSelectedY = -1;
        }
    }
}
