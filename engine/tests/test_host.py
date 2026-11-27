import json,logging,threading,time,unittest,tempfile
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
 def test_gta_game_clock_authority_survives_host_restart_without_wallclock_catchup(self):
  with tempfile.TemporaryDirectory() as folder:
   first=State("T"*48,folder)
   hello={"protocol":PROTOCOL,"request_id":"persist-clock-hello","session_id":"persist-clock-1",
          "adapter":{"id":"gta5.shvdn","version":"0.1.0"},"capabilities":["adapter.session"],
          "world_minute":600}
   self.assertTrue(first.handshake(hello)["accepted"])
   self.assertTrue(first.uses_game_clock())
   self.assertTrue(first.heartbeat("persist-clock-1",605))
   self.assertEqual(first.simulation.simulation_minute,605)
   self.assertTrue(first.close("persist-clock-1"))
   first.simulation.store.close()
   second=State("T"*48,folder)
   try:
    self.assertTrue(second.uses_game_clock())
    self.assertTrue(second.handshake({**hello,"request_id":"persist-clock-hello-2",
                                      "session_id":"persist-clock-2","world_minute":610})["accepted"])
    self.assertEqual(second.simulation.simulation_minute,610)
    self.assertEqual(second.simulation.store.get_meta("last_game_world_minute"),"610")
   finally:
    second.simulation.store.close()
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
 def test_voice_dialogue_memory_endpoint(self):
  npc={"npc_id":"npc.voice","display_name":"Mai","home_location_id":"home.voice","occupation":"seller",
       "schedule":[{"start_minute":0,"end_minute":1440,"activity":"work","location_id":"shop.voice"}]}
  self.assertEqual(self.req("/engine/v1/simulation/npcs",{"request_id":"voice-register","npc":npc})[0],201)
  payload={"request_id":"voice-memory-1","npc_id":"npc.voice","player_id":"player.main",
           "dialogue":{"player_message":"Quán đông không?","npc_reply":"Cũng hơi đông."}}
  code,result=self.req("/engine/v1/simulation/memory",payload)
  self.assertEqual(code,200); self.assertTrue(result["memory_recorded"])
  code,replay=self.req("/engine/v1/simulation/memory",payload)
  self.assertEqual(code,200); self.assertEqual(replay,result)
  state=self.state.simulation.npcs["npc.voice"]
  self.assertEqual(state.memories[-2]["summary"],"Quán đông không?")
  self.assertEqual(state.memories[-1]["summary"],"Cũng hơi đông.")
 def test_native_business_snapshot_sync_endpoint(self):
  self.assertEqual(self.req("/engine/v1/world/buildings",{
   "request_id":"sync-building","building_id":"building.sync","category":"commercial","name":"Sync shop",
   "adapter_maps":{"gta5.shvdn":{"entrance":"shop.sync.door"}},"interactions":["enter"]})[0],201)
  self.req("/engine/v1/world/accounts/ensure",{"request_id":"sync-owner","account_id":"account.sync.owner",
   "owner_id":"player.main","opening_balance":0,"allow_negative":True})
  self.req("/engine/v1/world/accounts/ensure",{"request_id":"sync-cash","account_id":"account.sync.cash",
   "owner_id":"business.sync","opening_balance":50,"allow_negative":True})
  self.assertEqual(self.req("/engine/v1/world/properties",{"request_id":"sync-property","property_id":"property.sync",
   "building_id":"building.sync","owner_account_id":"account.sync.owner","value":500,"rent":0})[0],201)
  self.assertEqual(self.req("/engine/v1/world/businesses",{"request_id":"sync-business","business_id":"business.sync",
   "property_id":"property.sync","owner_account_id":"account.sync.owner","cash_account_id":"account.sync.cash",
   "category":"retail"})[0],201)
  payload={"request_id":"sync-state-1","business_id":"business.sync","adapter_id":"gta5.shvdn",
           "cash_balance":75,"inventory":{"stock":12},"is_open":True}
  code,result=self.req("/engine/v1/world/businesses/sync",payload)
  self.assertEqual(code,200); self.assertEqual(result["cash_delta"],25)
  code,replay=self.req("/engine/v1/world/businesses/sync",payload)
  self.assertEqual(code,200); self.assertEqual(replay,result)
  self.assertEqual(self.state.society.account("account.sync.cash")["balance"],75)
  code,snapshot=self.req("/engine/v1/world/snapshot")
  self.assertEqual(code,200)
  self.assertEqual(snapshot["businesses"][0]["inventory"]["stock"],12)
 def test_context_endpoint_exposes_bounded_canonical_facts(self):
  npc={"npc_id":"npc.context-api","display_name":"Mai","home_location_id":"home.api","occupation":"nurse",
       "age_years":29,"household_id":"household.1","biography":"Works at the clinic.",
       "skills":{"medical":70},"knowledge":[{"fact":"Clinic closes at 18:00","source":"observation.1","confidence":0.9}],
       "schedule":[{"start_minute":0,"end_minute":1440,"activity":"work","location_id":"clinic.api"}]}
  code,r=self.req("/engine/v1/simulation/npcs",{"request_id":"context-register","npc":npc})
  self.assertEqual(code,201)
  code,context=self.req("/engine/v1/simulation/context",{"request_id":"context-read","npc_id":"npc.context-api","player_id":"player.1","topic":"clinic"})
  self.assertEqual(code,200)
  self.assertEqual(context["identity"]["age_years"],29)
  self.assertEqual(context["identity"]["skills"]["medical"],70)
  self.assertEqual(context["known_facts"][0]["source"],"observation.1")
  self.assertIn("Do not claim unlisted events",context["knowledge_boundary"])
  code,r=self.req("/engine/v1/simulation/context",{"request_id":"context-missing","npc_id":"missing","player_id":"player.1"})
  self.assertEqual(code,404)
 def test_second_game_adapter_uses_same_host_contract_and_domain_state(self):
  hello={"protocol":PROTOCOL,"request_id":"godot-hello","session_id":"godot-session",
         "adapter":{"id":"godot4.reference","version":"0.1.0","base_game":"Godot 4","base_game_build":"4.x"},
         "capabilities":["adapter.session","adapter.command_poll","adapter.actor_activity","adapter.actor_interaction"]}
  code,r=self.req("/engine/v1/handshake",hello); self.assertEqual(code,200); self.assertTrue(r["accepted"])
  npc={"npc_id":"npc.godot.minh-an","display_name":"Minh An","home_location_id":"home.godot","occupation":"barista",
       "traits":["friendly","disciplined"],"life_goal":"career",
       "schedule":[{"start_minute":0,"end_minute":1440,"activity":"work","location_id":"work.godot"}]}
  code,r=self.req("/engine/v1/simulation/npcs",{"request_id":"godot-register","npc":npc})
  self.assertEqual(code,201); self.assertTrue(r["registered"])
  code,r=self.req("/engine/v1/simulation/advance",{"request_id":"godot-advance","minutes":1})
  self.assertEqual(code,200)
  commands=self.req("/engine/v1/adapter/commands")[1]["commands"]
  self.assertEqual(len(commands),1)
  command=commands[0]
  self.assertEqual(command["command_type"],"actor.set_activity")
  self.assertEqual(command["payload"]["location_id"],"work.godot")
  code,r=self.req("/engine/v1/adapter/commands/ack",{"request_id":"godot-ack","command_id":command["command_id"],
       "result":{"success":True,"status":"completed","reason":"destination_reached","adapter_id":"godot4.reference"}})
  self.assertEqual(code,200); self.assertTrue(r["accepted"])
  code,r=self.req("/engine/v1/simulation/interact",{"request_id":"godot-talk","npc_id":"npc.godot.minh-an","player_id":"player.godot"})
  self.assertEqual(code,200); self.assertTrue(r["memory_recorded"])
  snapshot=self.req("/engine/v1/simulation/snapshot")[1]
  self.assertEqual(snapshot["npc_count"],1)
  self.assertEqual(snapshot["npcs"][0]["memory_count"],1)
  self.assertEqual(snapshot["npcs"][0]["relationships"],{"player.godot":2.0})
 def test_world_clock_advance_decays_traffic_once_even_on_request_replay(self):
  self.state.society.register_traffic_segment("road.clock","arterial",1.0)
  self.state.society.record_traffic_incident("incident.clock","road.clock",2,"collision","")
  self.assertEqual(self.state.society.snapshot()["traffic"][0]["congestion"],24.0)
  first=self.state.advance(10,"world-clock-advance")
  self.assertEqual(first["simulation_minute"],10)
  self.assertEqual(self.state.society.snapshot()["traffic"][0]["congestion"],19.0)
  replay=self.state.advance(10,"world-clock-advance")
  self.assertEqual(replay["simulation_minute"],10)
  self.assertEqual(self.state.society.snapshot()["traffic"][0]["congestion"],19.0)
 def test_world_interaction_and_society_apis(self):
  code,r=self.req("/engine/v1/world/buildings",{"request_id":"bldg-1","building_id":"home.1","category":"residential","name":"Home 1",
    "adapter_maps":{"godot4.reference":{"entrance":"home.1.door","interior":"home.1.room"}},"interactions":["enter","exit"]})
  self.assertEqual(code,201); self.assertTrue(r["registered"])
  code,r=self.req("/engine/v1/world/objects",{"request_id":"obj-1","object_id":"home.1.bed","building_id":"home.1",
    "kind":"bed","adapter_anchors":{"godot4.reference":"home.1.bed.anchor"}})
  self.assertEqual(code,201)
  code,r=self.req("/engine/v1/world/interact",{"request_id":"world-enter","actor_id":"player.godot",
    "target_kind":"building","target_id":"home.1","action":"enter","adapter_id":"godot4.reference"})
  self.assertEqual(code,202); self.assertTrue(r["accepted"])
  commands=self.req("/engine/v1/world/commands")[1]["commands"]
  self.assertEqual(len(commands),1); self.assertEqual(commands[0]["command_type"],"space.transition")
  code,r=self.req("/engine/v1/world/commands/ack",{"request_id":"world-ack","command_id":commands[0]["command_id"],
    "result":{"success":True,"status":"completed","outcome":"entered"}})
  self.assertEqual(code,200); self.assertTrue(r["accepted"])
  self.assertEqual(self.req("/engine/v1/world/snapshot")[1]["buildings"][0]["building_id"],"home.1")
  code,r=self.req("/engine/v1/world/accounts",{"request_id":"acct-1","account_id":"player.cash","owner_id":"player.1","opening_balance":1000})
  self.assertEqual(code,201); self.assertEqual(r["balance"],1000)
  code,r=self.req("/engine/v1/world/events",{"request_id":"legacy-events","source_id":"gta5.shvdn","events":[
    {"sequence":1,"event_type":"ledger.income","data":"amount=100;cat=work;note=shift","sim_minute":1}]})
  self.assertEqual(code,200); self.assertEqual(r["accepted"],1)
  code,r=self.req("/engine/v1/world/events",{"request_id":"legacy-events-replay","source_id":"gta5.shvdn","events":[
    {"sequence":1,"event_type":"ledger.income","data":"amount=100;cat=work;note=shift","sim_minute":1}]})
  self.assertEqual(code,200); self.assertEqual(r["duplicates"],1)
  self.assertEqual(self.state.society.account("legacy:player_cash")["balance"],100)
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

 def test_player_actor_registration_uses_same_canonical_life_engine(self):
  actor={"npc_id":"player.main","display_name":"Player","home_location_id":"home.player",
         "occupation":"citizen","actor_kind":"player","age_years":34}
  code,result=self.req("/engine/v1/simulation/actors",{"request_id":"player-register","npc":actor})
  self.assertEqual(code,201); self.assertTrue(result["registered"])
  self.assertEqual(result["npc_id"],"player.main")
  snapshot=self.req("/engine/v1/simulation/snapshot")[1]
  player=next(x for x in snapshot["npcs"] if x["npc_id"]=="player.main")
  self.assertEqual(player["actor_kind"],"player")
  self.assertIn("hygiene",player["needs"])
  self.assertIn("bladder",player["needs"])

 def test_confirmed_physical_object_use_changes_player_life_needs(self):
  actor={"npc_id":"player.physical","display_name":"Player","home_location_id":"home.physical",
         "occupation":"citizen","actor_kind":"player"}
  self.assertEqual(self.req("/engine/v1/simulation/actors",{"request_id":"physical-player","npc":actor})[0],201)
  self.state.simulation.npcs["player.physical"].bladder=95
  self.assertEqual(self.req("/engine/v1/world/buildings",{"request_id":"physical-building","building_id":"home.physical",
   "category":"residential","name":"Home","adapter_maps":{"godot4.reference":{"entrance":"door","interior":"room"}},
   "interactions":["enter"]})[0],201)
  self.assertEqual(self.req("/engine/v1/world/objects",{"request_id":"physical-toilet","object_id":"home.physical.toilet",
   "building_id":"home.physical","kind":"toilet","adapter_anchors":{"godot4.reference":"toilet.anchor"}})[0],201)
  code,result=self.req("/engine/v1/world/interact",{"request_id":"use-toilet","actor_id":"player.physical",
   "target_kind":"object","target_id":"home.physical.toilet","action":"use","adapter_id":"godot4.reference"})
  self.assertEqual(code,202)
  command=self.req("/engine/v1/world/commands")[1]["commands"][0]
  code,ack=self.req("/engine/v1/world/commands/ack",{"request_id":"toilet-ack","command_id":command["command_id"],
   "result":{"success":True,"status":"completed"}})
  self.assertEqual(code,200); self.assertTrue(ack["accepted"])
  snapshot=self.req("/engine/v1/simulation/snapshot")[1]
  player=next(x for x in snapshot["npcs"] if x["npc_id"]=="player.physical")
  self.assertEqual(player["needs"]["bladder"],5.0)

 def test_work_hours_settle_contract_payroll_once(self):
  npc={"npc_id":"npc.worker","display_name":"Worker","home_location_id":"home.worker","occupation":"clerk",
       "schedule":[{"start_minute":0,"end_minute":1440,"activity":"work","location_id":"shop.worker"}]}
  self.assertEqual(self.req("/engine/v1/simulation/npcs",{"request_id":"worker-register","npc":npc})[0],201)
  self.req("/engine/v1/world/accounts/ensure",{"request_id":"pay-owner","account_id":"pay.owner","owner_id":"owner","opening_balance":0,"allow_negative":True})
  self.req("/engine/v1/world/accounts/ensure",{"request_id":"pay-cash","account_id":"pay.cash","owner_id":"business","opening_balance":1000,"allow_negative":True})
  self.req("/engine/v1/world/accounts/ensure",{"request_id":"pay-employee","account_id":"pay.employee","owner_id":"npc.worker","opening_balance":0,"allow_negative":True})
  self.req("/engine/v1/world/buildings",{"request_id":"pay-building","building_id":"building.pay","category":"commercial","name":"Shop",
       "adapter_maps":{"godot4.reference":{"entrance":"shop.door"}},"interactions":["enter"]})
  self.req("/engine/v1/world/properties",{"request_id":"pay-property","property_id":"property.pay","building_id":"building.pay",
       "owner_account_id":"pay.owner","value":1000,"rent":0})
  self.req("/engine/v1/world/businesses",{"request_id":"pay-business","business_id":"business.pay","property_id":"property.pay",
       "owner_account_id":"pay.owner","cash_account_id":"pay.cash","category":"retail"})
  code,contract=self.req("/engine/v1/world/employment/contracts",{"request_id":"pay-contract","contract_id":"contract.worker",
       "employee_id":"npc.worker","employee_account_id":"pay.employee","business_id":"business.pay","hourly_wage":50})
  self.assertEqual(code,201); self.assertTrue(contract["created"])
  code,result=self.req("/engine/v1/simulation/advance",{"request_id":"work-hour-1","minutes":60})
  self.assertEqual(code,200)
  self.assertEqual(self.state.society.account("pay.employee")["balance"],50)
  self.assertEqual(self.state.society.account("pay.cash")["balance"],950)
  self.req("/engine/v1/simulation/advance",{"request_id":"work-hour-1","minutes":60})
  self.assertEqual(self.state.society.account("pay.employee")["balance"],50)

 def test_family_and_social_relationships_are_canonical_for_player_and_npc(self):
  player={"npc_id":"player.family","display_name":"Player","home_location_id":"home.family",
          "occupation":"citizen","actor_kind":"player","household_id":"household.family"}
  npc={"npc_id":"npc.family","display_name":"Relative","home_location_id":"home.family",
       "occupation":"teacher","household_id":"household.family"}
  self.req("/engine/v1/simulation/actors",{"request_id":"family-player","npc":player})
  self.req("/engine/v1/simulation/actors",{"request_id":"family-npc","npc":npc})
  code,relationship=self.req("/engine/v1/world/social/relationships",{"request_id":"family-link",
       "actor_id":"player.family","target_id":"npc.family","relation_type":"sibling","strength":85})
  self.assertEqual(code,200); self.assertEqual(relationship["relation_type"],"sibling")
  snapshot=self.req("/engine/v1/world/snapshot")[1]
  self.assertEqual(snapshot["relationships"][0]["actor_id"],"player.family")
  player_state=self.state.simulation.npcs["player.family"]
  npc_state=self.state.simulation.npcs["npc.family"]
  self.assertEqual(player_state.affinity["npc.family"],85.0)
  self.assertEqual(npc_state.affinity["player.family"],85.0)

if __name__=="__main__":unittest.main()
