"""Authenticated loopback Engine Host session gateway (Python stdlib only)."""
import argparse, json, logging, secrets, threading, time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

try:
    from .life_simulation import LifeSimulationEngine, NPCProfile, ScheduleBlock, SQLiteLifeStore
except ImportError:  # PyInstaller entry-point execution
    from life_simulation import LifeSimulationEngine, NPCProfile, ScheduleBlock, SQLiteLifeStore

PROTOCOL="gvl-engine/1"; BUILD="0.1.0"; MAX_BODY=65536
class State:
    def __init__(self, token, data_dir=None):
        self.token=token; self.lock=threading.RLock(); self.session=None; self.last_world_minute=None; self.started=time.time()
        self.data_dir = Path(data_dir) if data_dir else None
        db_path = str(self.data_dir / "life-simulation.sqlite3") if self.data_dir else ":memory:"
        self.simulation = LifeSimulationEngine(SQLiteLifeStore(db_path))
    def auth(self, token): return bool(token) and secrets.compare_digest(self.token, token)
    def handshake(self, p):
        rid=p.get("request_id")
        if not isinstance(rid,str) or not rid: raise ValueError("request_id_required")
        if p.get("protocol") != PROTOCOL: raise ValueError("protocol_mismatch")
        a=p.get("adapter")
        if not isinstance(a,dict) or not a.get("id") or not a.get("version"): raise ValueError("invalid_adapter_identity")
        sid=p.get("session_id")
        if not isinstance(sid,str) or not sid or len(sid)>128: raise ValueError("invalid_session_id")
        caps=p.get("capabilities",[])
        if not isinstance(caps,list) or any(not isinstance(x,str) for x in caps): raise ValueError("invalid_capabilities")
        world_minute=p.get("world_minute")
        if world_minute is not None and (isinstance(world_minute,bool) or not isinstance(world_minute,int) or world_minute<0):
            raise ValueError("invalid_world_minute")
        with self.lock:
            self.session={"session_id":sid,"adapter_id":a["id"],"adapter_version":a["version"],
              "base_game":a.get("base_game","unknown"),"base_game_build":a.get("base_game_build","unknown"),
              "capabilities":sorted(set(caps)),"connected_at":time.time(),"last_heartbeat":time.time()}
            self.last_world_minute=world_minute
            if world_minute is not None:
                self.simulation.set_clock_baseline(world_minute)
        return {"accepted":True,"protocol":PROTOCOL,"session_id":sid,"required_capabilities":["adapter.session"],"host_build":BUILD}
    def heartbeat(self,sid,world_minute=None):
        if world_minute is not None and (isinstance(world_minute,bool) or not isinstance(world_minute,int) or world_minute<0):
            raise ValueError("invalid_world_minute")
        with self.lock:
            if not self.session or self.session["session_id"]!=sid: return False
            if world_minute is not None:
                previous=self.last_world_minute
                if previous is not None and world_minute>previous:
                    self.simulation.advance(min(world_minute-previous,10080))
                # A backward game-clock adjustment never rewinds durable NPC history.
                self.last_world_minute=world_minute
                # Refresh the offline checkpoint even while the game is paused.
                self.simulation.store.save(self.simulation.simulation_minute,[],[])
            self.session["last_heartbeat"]=time.time(); return True
    def adapter_is_connected(self):
        with self.lock:
            return bool(self.session and self.last_world_minute is not None and time.time()-self.session["last_heartbeat"]<=15)
    def close(self,sid):
        with self.lock:
            if self.session and self.session["session_id"]==sid:
                self.session=None; self.last_world_minute=None; return True
            return False
    def snapshot(self):
        with self.lock: s=dict(self.session) if self.session else None
        connected=bool(s and time.time()-s["last_heartbeat"]<=15)
        return {"service":"gta-viet-life-engine","status":"healthy","build":BUILD,"protocol":PROTOCOL,
          "uptime_seconds":int(time.time()-self.started),"adapter_connected":connected,"adapter_session":s if connected else None}

def handler_for(state, log):
    class H(BaseHTTPRequestHandler):
        def log_message(self,fmt,*args): log.info("%s %s",self.client_address[0],fmt%args)
        def sendj(self,code,obj):
            b=json.dumps(obj,separators=(",",":")).encode()
            self.send_response(code); self.send_header("Content-Type","application/json"); self.send_header("Content-Length",str(len(b))); self.send_header("Cache-Control","no-store"); self.end_headers(); self.wfile.write(b)
        def body(self):
            n=int(self.headers.get("Content-Length","0"))
            if n<=0 or n>MAX_BODY: raise ValueError("invalid_body_size")
            p=json.loads(self.rfile.read(n))
            if not isinstance(p,dict): raise ValueError("json_object_required")
            return p
        def do_GET(self):
            if self.path=="/health": return self.sendj(200,{"status":"healthy","build":BUILD})
            if self.path=="/engine/v1/status":
                if not state.auth(self.headers.get("X-GVL-Token")): return self.sendj(401,{"error":"unauthorized"})
                return self.sendj(200,state.snapshot())
            if self.path=="/engine/v1/simulation/snapshot":
                if not state.auth(self.headers.get("X-GVL-Token")): return self.sendj(401,{"error":"unauthorized"})
                return self.sendj(200,state.simulation.snapshot())
            if self.path=="/engine/v1/adapter/commands":
                if not state.auth(self.headers.get("X-GVL-Token")): return self.sendj(401,{"error":"unauthorized"})
                try:
                    limit=int(self.headers.get("X-GVL-Limit","50"))
                    return self.sendj(200,{"commands":state.simulation.pending_commands(limit)})
                except (ValueError,TypeError) as exc:
                    return self.sendj(400,{"error":str(exc)})
            return self.sendj(404,{"error":"not_found"})
        def do_POST(self):
            if not state.auth(self.headers.get("X-GVL-Token")): return self.sendj(401,{"error":"unauthorized"})
            try:
                p=self.body()
                rid=p.get("request_id")
                if not isinstance(rid,str) or not rid: raise ValueError("request_id_required")
                if self.path=="/engine/v1/handshake": return self.sendj(200,state.handshake(p))
                if self.path=="/engine/v1/simulation/advance":
                    result=state.simulation.advance(p.get("minutes"), rid)
                    return self.sendj(200,result)
                if self.path=="/engine/v1/adapter/commands/ack":
                    command_id=p.get("command_id")
                    result=p.get("result")
                    accepted=state.simulation.complete_command(command_id,result)
                    return self.sendj(200 if accepted else 409,{"accepted":accepted,"command_id":command_id})
                if self.path=="/engine/v1/simulation/npcs":
                    raw=p.get("npc")
                    if not isinstance(raw,dict): raise ValueError("npc_object_required")
                    schedule=raw.get("schedule",[])
                    if not isinstance(schedule,list) or any(not isinstance(item,dict) for item in schedule):
                        raise ValueError("schedule_array_required")
                    blocks=[ScheduleBlock(**item) for item in schedule]
                    profile=NPCProfile(
                        npc_id=raw.get("npc_id",""), display_name=raw.get("display_name",""),
                        home_location_id=raw.get("home_location_id",""), occupation=raw.get("occupation",""),
                        traits=tuple(raw.get("traits",[])), schedule=tuple(blocks),
                        life_goal=raw.get("life_goal","career"))
                    state.simulation.register_npc(profile)
                    return self.sendj(201,{"registered":True,"npc_id":profile.npc_id})
                if self.path=="/engine/v1/simulation/interact":
                    result=state.simulation.interact(
                        p.get("npc_id",""),p.get("player_id",""),p.get("interaction","talk"),rid)
                    return self.sendj(200,result)
                if self.path=="/engine/v1/heartbeat":
                    ok=state.heartbeat(str(p.get("session_id","")),p.get("world_minute")); return self.sendj(200 if ok else 409,{"accepted":ok})
                if self.path=="/engine/v1/close":
                    return self.sendj(200,{"closed":state.close(str(p.get("session_id","")))})
                if self.path=="/engine/v1/shutdown":
                    self.sendj(200,{"stopping":True}); threading.Thread(target=self.server.shutdown,daemon=True).start(); return
                return self.sendj(404,{"error":"not_found"})
            except KeyError as e: return self.sendj(404,{"error":str(e).strip("'")})
            except (ValueError,TypeError,json.JSONDecodeError) as e: return self.sendj(400,{"error":str(e)})
            except Exception:
                log.exception("request failed"); return self.sendj(500,{"error":"internal_error"})
    return H

def main():
    ap=argparse.ArgumentParser(); ap.add_argument("--host",default="127.0.0.1"); ap.add_argument("--port",type=int,default=5580)
    ap.add_argument("--token-file",required=True); ap.add_argument("--data-dir",required=True); a=ap.parse_args()
    if a.host not in ("127.0.0.1","::1","localhost"): raise SystemExit("loopback binding required")
    token=Path(a.token_file).read_text(encoding="utf-8").strip()
    if len(token)<32: raise SystemExit("invalid local token")
    d=Path(a.data_dir); d.mkdir(parents=True,exist_ok=True)
    logging.basicConfig(filename=str(d/"engine-host.log"),level=logging.INFO,format="%(asctime)s %(levelname)s %(message)s",encoding="utf-8")
    log=logging.getLogger("gvl.engine")
    state=State(token,d)
    try:
        last_epoch=state.simulation.store.get_wallclock_epoch()
        elapsed=max(0.0,time.time()-last_epoch) if last_epoch is not None else 0.0
        catchup=min(int(elapsed/2.0),10080)
        if catchup:
            state.simulation.advance(catchup)
            log.info("Recovered %s offline simulation minutes (cap 10080)",catchup)
        else:
            state.simulation.store.save(state.simulation.simulation_minute,[],[])
    except (ValueError,TypeError) as exc:
        log.warning("Offline simulation clock recovery skipped: %s",exc)
    stop=threading.Event()
    def simulation_worker():
        while not stop.wait(2.0):
            try:
                if not state.adapter_is_connected():
                    state.simulation.advance(1)
            except Exception:
                log.exception("background life simulation tick failed")
    worker=threading.Thread(target=simulation_worker,name="gvl-life-simulation",daemon=True)
    worker.start()
    s=ThreadingHTTPServer((a.host,a.port),handler_for(state,log)); s.daemon_threads=True
    log.info("Engine Host %s listening on %s:%s",BUILD,a.host,a.port)
    try: s.serve_forever(poll_interval=.25)
    finally:
        stop.set(); worker.join(timeout=3)
        s.server_close(); log.info("Engine Host stopped")
if __name__=="__main__": main()
