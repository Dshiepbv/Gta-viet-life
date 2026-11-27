import json,logging,threading,time,unittest
from http.server import ThreadingHTTPServer
from urllib.request import Request,urlopen
from urllib.error import HTTPError
from engine.host import State,handler_for,PROTOCOL
class HostTests(unittest.TestCase):
 def setUp(self):
  self.state=State("T"*48); self.server=ThreadingHTTPServer(("127.0.0.1",0),handler_for(self.state,logging.getLogger("test")))
  threading.Thread(target=self.server.serve_forever,daemon=True).start(); self.base="http://127.0.0.1:%d"%self.server.server_address[1]
 def tearDown(self): self.server.shutdown(); self.server.server_close()
 def req(self,path,p=None,auth=True):
  h={"Content-Type":"application/json"}
  if auth:h["X-GVL-Token"]="T"*48
  q=Request(self.base+path,data=json.dumps(p).encode() if p is not None else None,headers=h)
  try:
   with urlopen(q,timeout=2) as r:return r.status,json.loads(r.read())
  except HTTPError as e:return e.code,json.loads(e.read())
 def test_health_and_auth(self):
  self.assertEqual(self.req("/health",auth=False)[0],200); self.assertEqual(self.req("/engine/v1/status",auth=False)[0],401)
 def test_handshake_heartbeat(self):
  p={"protocol":PROTOCOL,"request_id":"req-1","session_id":"s1","adapter":{"id":"gta5.shvdn","version":"0.1.0"},"capabilities":["adapter.session"]}
  code,r=self.req("/engine/v1/handshake",p); self.assertEqual(code,200); self.assertTrue(r["accepted"])
  self.assertTrue(self.state.snapshot()["adapter_connected"]); self.assertTrue(self.state.heartbeat("s1"))
 def test_protocol_rejected(self):
  code,r=self.req("/engine/v1/handshake",{"protocol":"bad","request_id":"req-bad","session_id":"s","adapter":{"id":"x","version":"1"}})
  self.assertEqual(code,400); self.assertEqual(r["error"],"protocol_mismatch")
 def test_stale_session(self):
  self.state.handshake({"protocol":PROTOCOL,"request_id":"req-stale","session_id":"s","adapter":{"id":"x","version":"1"},"capabilities":[]})
  self.state.session["last_heartbeat"]=time.time()-20; self.assertFalse(self.state.snapshot()["adapter_connected"])
 def test_life_simulation_api_register_advance_and_interact(self):
  npc={"npc_id":"npc.api","display_name":"Lan","home_location_id":"home.api","occupation":"teacher",
       "schedule":[{"start_minute":0,"end_minute":1440,"activity":"work","location_id":"school.api"}]}
  code,r=self.req("/engine/v1/simulation/npcs",{"request_id":"npc-1","npc":npc})
  self.assertEqual(code,201); self.assertTrue(r["registered"])
  code,r=self.req("/engine/v1/simulation/advance",{"request_id":"tick-1","minutes":5})
  self.assertEqual(code,200); self.assertEqual(r["simulation_minute"],5)
  code,again=self.req("/engine/v1/simulation/advance",{"request_id":"tick-1","minutes":5})
  self.assertEqual(code,200); self.assertEqual(again["simulation_minute"],5)
  code,r=self.req("/engine/v1/simulation/interact",{"request_id":"talk-1","npc_id":"npc.api","player_id":"player.1"})
  self.assertEqual(code,200); self.assertTrue(r["memory_recorded"])
  code,again=self.req("/engine/v1/simulation/interact",{"request_id":"talk-1","npc_id":"npc.api","player_id":"player.1"})
  self.assertEqual(code,200); self.assertEqual(again["affinity"],2.0)
  code,r=self.req("/engine/v1/simulation/snapshot")
  self.assertEqual(code,200); self.assertEqual(r["npc_count"],1)
  self.assertEqual(r["npcs"][0]["memory_count"],1)
if __name__=="__main__":unittest.main()
