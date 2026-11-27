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
  self.assertTrue(self.state.snapshot()["adapter_connected"]); self.assertFalse(self.state.adapter_is_connected())
  self.assertTrue(self.state.heartbeat("s1"))
 def test_game_clock_drives_simulation_and_pauses_background_clock(self):
  p={"protocol":PROTOCOL,"request_id":"clock-handshake","session_id":"clock-session",
     "adapter":{"id":"gta5.shvdn","version":"0.1.0"},"capabilities":["adapter.session"],"world_minute":600}
  self.assertTrue(self.state.handshake(p)["accepted"])
  self.assertEqual(self.state.simulation.simulation_minute,600)
  code,r=self.req("/engine/v1/heartbeat",{"request_id":"clock-heartbeat","session_id":"clock-session","world_minute":603})
  self.assertEqual(code,200); self.assertTrue(r["accepted"])
  self.assertEqual(self.state.simulation.simulation_minute,603)
  self.assertTrue(self.state.heartbeat("clock-session",603))
  self.assertEqual(self.state.simulation.simulation_minute,603)
  # A backward clock correction updates the baseline but never rewinds NPC history.
  self.assertTrue(self.state.heartbeat("clock-session",590))
  self.assertEqual(self.state.simulation.simulation_minute,603)
  self.assertTrue(self.state.heartbeat("clock-session",592))
  self.assertEqual(self.state.simulation.simulation_minute,605)
  self.assertTrue(self.state.adapter_is_connected())
  self.assertTrue(self.state.close("clock-session"))
  self.assertFalse(self.state.adapter_is_connected())
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
 def test_adapter_command_poll_and_ack(self):
  npc={"npc_id":"npc.command","display_name":"Minh","home_location_id":"home.command","occupation":"worker",
       "schedule":[{"start_minute":0,"end_minute":1440,"activity":"work","location_id":"work.command"}]}
  self.assertEqual(self.req("/engine/v1/simulation/npcs",{"request_id":"npc-command","npc":npc})[0],201)
  self.assertEqual(self.req("/engine/v1/simulation/advance",{"request_id":"advance-command","minutes":1})[0],200)
  code,r=self.req("/engine/v1/adapter/commands")
  self.assertEqual(code,200); self.assertEqual(len(r["commands"]),1)
  command=r["commands"][0]
  self.assertEqual(command["command_type"],"actor.set_activity")
  code,ack=self.req("/engine/v1/adapter/commands/ack",{"request_id":"ack-command","command_id":command["command_id"],"result":{"success":True}})
  self.assertEqual(code,200); self.assertTrue(ack["accepted"])
  self.assertEqual(self.req("/engine/v1/adapter/commands")[1]["commands"],[])
  npc2={"npc_id":"npc.unsupported","display_name":"An","home_location_id":"home.unsupported","occupation":"worker",
        "schedule":[{"start_minute":0,"end_minute":1440,"activity":"work","location_id":"unknown.location"}]}
  self.assertEqual(self.req("/engine/v1/simulation/npcs",{"request_id":"npc-unsupported","npc":npc2})[0],201)
  self.assertEqual(self.req("/engine/v1/simulation/advance",{"request_id":"advance-unsupported","minutes":1})[0],200)
  commands=self.req("/engine/v1/adapter/commands")[1]["commands"]
  self.assertEqual(len(commands),1)
  code,report=self.req("/engine/v1/adapter/commands/ack",{"request_id":"report-unsupported","command_id":commands[0]["command_id"],
      "result":{"success":False,"status":"unsupported","reason":"location_not_mapped"}})
  self.assertEqual(code,200); self.assertTrue(report["accepted"])
  self.assertEqual(self.req("/engine/v1/adapter/commands")[1]["commands"],[])
if __name__=="__main__":unittest.main()
