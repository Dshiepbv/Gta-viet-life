"""Authenticated loopback Engine Host session gateway (Python stdlib only)."""
import argparse, json, logging, secrets, threading, time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

PROTOCOL="gvl-engine/1"; BUILD="0.1.0"; MAX_BODY=65536
class State:
    def __init__(self, token):
        self.token=token; self.lock=threading.RLock(); self.session=None; self.started=time.time()
    def auth(self, token): return bool(token) and secrets.compare_digest(self.token, token)
    def handshake(self, p):
        if p.get("protocol") != PROTOCOL: raise ValueError("protocol_mismatch")
        a=p.get("adapter")
        if not isinstance(a,dict) or not a.get("id") or not a.get("version"): raise ValueError("invalid_adapter_identity")
        sid=p.get("session_id")
        if not isinstance(sid,str) or not sid or len(sid)>128: raise ValueError("invalid_session_id")
        caps=p.get("capabilities",[])
        if not isinstance(caps,list) or any(not isinstance(x,str) for x in caps): raise ValueError("invalid_capabilities")
        with self.lock:
            self.session={"session_id":sid,"adapter_id":a["id"],"adapter_version":a["version"],
              "base_game":a.get("base_game","unknown"),"base_game_build":a.get("base_game_build","unknown"),
              "capabilities":sorted(set(caps)),"connected_at":time.time(),"last_heartbeat":time.time()}
        return {"accepted":True,"protocol":PROTOCOL,"session_id":sid,"required_capabilities":["adapter.session"],"host_build":BUILD}
    def heartbeat(self,sid):
        with self.lock:
            if not self.session or self.session["session_id"]!=sid: return False
            self.session["last_heartbeat"]=time.time(); return True
    def close(self,sid):
        with self.lock:
            if self.session and self.session["session_id"]==sid: self.session=None; return True
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
            return self.sendj(404,{"error":"not_found"})
        def do_POST(self):
            if not state.auth(self.headers.get("X-GVL-Token")): return self.sendj(401,{"error":"unauthorized"})
            try:
                p=self.body()
                if self.path=="/engine/v1/handshake": return self.sendj(200,state.handshake(p))
                if self.path=="/engine/v1/heartbeat":
                    ok=state.heartbeat(str(p.get("session_id",""))); return self.sendj(200 if ok else 409,{"accepted":ok})
                if self.path=="/engine/v1/close":
                    return self.sendj(200,{"closed":state.close(str(p.get("session_id","")))})
                if self.path=="/engine/v1/shutdown":
                    self.sendj(200,{"stopping":True}); threading.Thread(target=self.server.shutdown,daemon=True).start(); return
                return self.sendj(404,{"error":"not_found"})
            except (ValueError,json.JSONDecodeError) as e: return self.sendj(400,{"error":str(e)})
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
    log=logging.getLogger("gvl.engine"); s=ThreadingHTTPServer((a.host,a.port),handler_for(State(token),log)); s.daemon_threads=True
    log.info("Engine Host %s listening on %s:%s",BUILD,a.host,a.port)
    try: s.serve_forever(poll_interval=.25)
    finally: s.server_close(); log.info("Engine Host stopped")
if __name__=="__main__": main()
