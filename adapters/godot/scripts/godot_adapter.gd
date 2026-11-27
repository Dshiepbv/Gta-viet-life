extends Node2D

const HOST := "http://127.0.0.1:5580"
const PROTOCOL := "gvl-engine/1"
const ADAPTER_ID := "godot4.reference"
const ADAPTER_VERSION := "0.1.0"
const NPC_ID := "npc.godot.minh-an"
const PLAYER_ID := "player.godot"
const ResidentScript = preload("res://scripts/resident.gd")
const POLL_SECONDS := 2.0

var token := ""
var session_id := ""
var request_counter := 0
var resident: Variant
var player: Node2D
var locations := {
	"home.godot": Vector2(170, 400),
	"home.interior.godot": Vector2(240, 260),
	"work.godot": Vector2(690, 190),
	"work.interior.godot": Vector2(760, 300),
	"park.godot": Vector2(470, 420),
	"bed.anchor.godot": Vector2(300, 260),
	"fridge.anchor.godot": Vector2(210, 310),
	"counter.anchor.godot": Vector2(740, 320)
}
var labels := {"home.godot": "HOME DOOR", "home.interior.godot": "HOME INTERIOR", "work.godot": "CAFE DOOR", "work.interior.godot": "CAFE INTERIOR", "park.godot": "PARK"}
var world_target_points := {"home.godot": "building.home.godot", "work.godot": "building.cafe.godot", "bed.anchor.godot": "object.bed.godot", "fridge.anchor.godot": "object.fridge.godot", "counter.anchor.godot": "object.counter.godot"}
var http: HTTPRequest
var status_text := "Waiting for Engine Host…"
var nearby := false
var busy := false

func _ready() -> void:
	token = OS.get_environment("GVL_ENGINE_TOKEN").strip_edges()
	session_id = "godot-" + str(Time.get_ticks_usec())
	http = HTTPRequest.new()
	add_child(http)
	resident = ResidentScript.new()
	resident.position = locations["home.godot"]
	add_child(resident)
	player = Node2D.new()
	player.position = Vector2(470, 300)
	add_child(player)
	set_process(true)
	_boot.call_deferred()

func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and not event.echo and event.keycode == KEY_E and not busy:
		_interact_nearest_world_target.call_deferred()

func _process(_delta: float) -> void:
	var movement := Input.get_vector("ui_left", "ui_right", "ui_up", "ui_down")
	player.position += movement * 190.0 * _delta
	queue_redraw()
	nearby = resident.global_position.distance_to(player.global_position) < 85.0
	if Input.is_action_just_pressed("ui_accept") and nearby and not busy:
		_interact.call_deferred()

func _draw() -> void:
	for location_id in labels:
		var p: Vector2 = locations[location_id]
		draw_rect(Rect2(p - Vector2(35, 25), Vector2(70, 50)), Color(0.18, 0.22, 0.27), true)
		draw_string(ThemeDB.fallback_font, p + Vector2(-34, 42), labels[location_id], HORIZONTAL_ALIGNMENT_LEFT, -1, 11, Color.WHITE)
	for anchor in ["bed.anchor.godot", "fridge.anchor.godot", "counter.anchor.godot"]:
		draw_circle(locations[anchor], 7.0, Color(0.2, 0.75, 0.85))
	draw_circle(player.position, 85.0, Color(0.2, 0.8, 0.5, 0.08))
	draw_circle(player.position, 10.0, Color(0.95, 0.72, 0.15))
	draw_string(ThemeDB.fallback_font, Vector2(20, 35), "GTA VIET LIFE — GODOT SECOND ADAPTER", HORIZONTAL_ALIGNMENT_LEFT, -1, 20, Color.WHITE)
	draw_string(ThemeDB.fallback_font, Vector2(20, 65), status_text, HORIZONTAL_ALIGNMENT_LEFT, -1, 15, Color(0.7, 0.9, 0.75))
	draw_string(ThemeDB.fallback_font, Vector2(20, 95), "Move with arrow keys. Approach the green social area and press Enter to talk.", HORIZONTAL_ALIGNMENT_LEFT, -1, 13, Color.WHITE)
	if resident:
		draw_string(ThemeDB.fallback_font, resident.position + Vector2(-38, -38), resident.display_name + " · " + resident.current_activity, HORIZONTAL_ALIGNMENT_LEFT, -1, 13, Color.WHITE)

func _next_request_id(prefix: String) -> String:
	request_counter += 1
	return "godot-" + prefix + "-" + str(request_counter) + "-" + str(Time.get_ticks_usec())

func _request(path: String, payload: Dictionary = {}) -> Dictionary:
	if token.length() < 32:
		status_text = "Set GVL_ENGINE_TOKEN to the local Engine Host token."
		return {}
	var headers := PackedStringArray(["Content-Type: application/json", "X-GVL-Token: " + token])
	var body := ""
	if not payload.is_empty():
		body = JSON.stringify(payload)
	var err := http.request(HOST + path, headers, HTTPClient.METHOD_GET if payload.is_empty() else HTTPClient.METHOD_POST, body)
	if err != OK:
		status_text = "HTTP request could not start: " + str(err)
		return {}
	var response: Array = await http.request_completed
	var code: int = response[1]
	if code < 200 or code >= 300:
		status_text = "Engine Host returned HTTP " + str(code)
		return {}
	var parsed = JSON.parse_string(response[3].get_string_from_utf8())
	if not parsed is Dictionary:
		status_text = "Engine Host returned invalid JSON."
		return {}
	return parsed

func _boot() -> void:
	if token.length() < 32:
		status_text = "Configure GVL_ENGINE_TOKEN before launch."
		return
	var hello := await _request("/engine/v1/handshake", {
		"request_id": _next_request_id("hello"),
		"protocol": PROTOCOL,
		"session_id": session_id,
		"adapter": {"id": ADAPTER_ID, "version": ADAPTER_VERSION, "base_game": "Godot 4", "base_game_build": Engine.get_version_info().get("string", "unknown")},
		"capabilities": ["adapter.session", "adapter.command_poll", "adapter.actor_activity", "adapter.actor_interaction"]
	})
	if not hello.get("accepted", false):
		status_text = "Engine Host handshake failed."
		return
	var profile := await _request("/engine/v1/simulation/npcs", {
		"request_id": _next_request_id("register"),
		"npc": {
			"npc_id": NPC_ID,
			"display_name": resident.display_name,
			"home_location_id": "home.godot",
			"occupation": "barista",
			"traits": ["friendly", "disciplined"],
			"life_goal": "career",
			"schedule": [
				{"start_minute": 0, "end_minute": 360, "activity": "sleep", "location_id": "home.godot"},
				{"start_minute": 360, "end_minute": 480, "activity": "commute", "location_id": "park.godot"},
				{"start_minute": 480, "end_minute": 1020, "activity": "work", "location_id": "work.godot"},
				{"start_minute": 1020, "end_minute": 1320, "activity": "leisure", "location_id": "park.godot"},
				{"start_minute": 1320, "end_minute": 1440, "activity": "sleep", "location_id": "home.godot"}
			]
		}
	})
	if profile.is_empty() or not profile.get("registered", false):
		status_text = "NPC registration failed (identity conflict or Host logs)."
		return
	if not await _register_world_catalog():
		status_text = "World catalog registration failed."
		return
	status_text = "Connected — NPC, building, door and object commands use the canonical Host."
	_run_adapter_loop.call_deferred()

func _run_adapter_loop() -> void:
	while is_inside_tree():
		await _heartbeat()
		await _poll_commands()
		await _poll_world_commands()
		await get_tree().create_timer(POLL_SECONDS).timeout

func _heartbeat() -> void:
	await _request("/engine/v1/heartbeat", {"request_id": _next_request_id("heartbeat"), "session_id": session_id})

func _register_world_catalog() -> bool:
	var home := await _request("/engine/v1/world/buildings", {
		"request_id": _next_request_id("building-home"),
		"building_id": "building.home.godot", "category": "residential", "name": "Minh An home",
		"adapter_maps": {ADAPTER_ID: {"entrance": "home.godot", "interior": "home.interior.godot"}},
		"interactions": ["enter", "exit", "inspect"]
	})
	if home.is_empty() or not home.get("registered", false):
		return false
	var cafe := await _request("/engine/v1/world/buildings", {
		"request_id": _next_request_id("building-cafe"),
		"building_id": "building.cafe.godot", "category": "workplace", "name": "Neighborhood cafe",
		"adapter_maps": {ADAPTER_ID: {"entrance": "work.godot", "interior": "work.interior.godot"}},
		"interactions": ["enter", "exit", "inspect", "open", "close"]
	})
	if cafe.is_empty() or not cafe.get("registered", false):
		return false
	for item in [
		{"id": "object.bed.godot", "building": "building.home.godot", "kind": "bed", "anchor": "bed.anchor.godot"},
		{"id": "object.fridge.godot", "building": "building.home.godot", "kind": "fridge", "anchor": "fridge.anchor.godot"},
		{"id": "object.counter.godot", "building": "building.cafe.godot", "kind": "counter", "anchor": "counter.anchor.godot"}
	]:
		var result := await _request("/engine/v1/world/objects", {
			"request_id": _next_request_id("object"),
			"object_id": item["id"], "building_id": item["building"], "kind": item["kind"],
			"adapter_anchors": {ADAPTER_ID: item["anchor"]}, "initial_state": {"uses": 0}
		})
		if result.is_empty() or not result.get("registered", false):
			return false
	return true

func _interact_nearest_world_target() -> void:
	var best_id := ""
	var best_kind := ""
	var best_action := ""
	var best_distance := 58.0
	for semantic in world_target_points:
		var d: float = player.global_position.distance_to(locations[semantic])
		if d < best_distance:
			best_distance = d
			best_id = world_target_points[semantic]
			best_kind = "building" if best_id.begins_with("building.") else "object"
			best_action = "enter" if best_kind == "building" else "use"
	if best_id == "":
		return
	var response := await _request("/engine/v1/world/interact", {
		"request_id": _next_request_id("world-interaction"),
		"actor_id": PLAYER_ID, "target_kind": best_kind, "target_id": best_id,
		"action": best_action, "adapter_id": ADAPTER_ID
	})
	if response.get("accepted", false):
		status_text = "World action queued: " + best_action + " " + best_id
	else:
		status_text = "World interaction rejected by Engine Host."

func _poll_world_commands() -> void:
	var result := await _request("/engine/v1/world/commands")
	for command in result.get("commands", []):
		var payload: Dictionary = command.get("payload", {})
		if payload.get("adapter_id", "") != ADAPTER_ID:
			continue
		var action: String = str(payload.get("action", ""))
		var semantic: String = str(payload.get("semantic_target", ""))
		var success := locations.has(semantic)
		var outcome := "unsupported"
		if success and command.get("command_type", "") == "space.transition":
			player.position = locations[semantic]
			outcome = "entered_or_exited_space"
			status_text = "Physical space transition completed: " + semantic
		elif success and command.get("command_type", "") == "interaction.use_object":
			player.position = locations[semantic]
			outcome = "object_interaction_executed"
			status_text = "Used world object: " + str(payload.get("target_id", ""))
		else:
			success = false
		await _request("/engine/v1/world/commands/ack", {
			"request_id": _next_request_id("world-ack"),
			"command_id": command.get("command_id", ""),
			"result": {"success": success, "status": "completed" if success else "unsupported",
				"outcome": outcome, "adapter_id": ADAPTER_ID}
		})

func _poll_commands() -> void:
	var result := await _request("/engine/v1/adapter/commands")
	for command in result.get("commands", []):
		if command.get("npc_id", "") != NPC_ID:
			continue
		var payload: Dictionary = command.get("payload", {})
		var location_id: String = payload.get("location_id", "")
		if not locations.has(location_id):
			await _ack(command.get("command_id", ""), false, "unsupported", "location_not_mapped")
			continue
		busy = true
		await resident.move_to(locations[location_id], str(payload.get("activity", "idle")))
		await _ack(command.get("command_id", ""), true, "completed", "destination_reached")
		busy = false

func _ack(command_id: String, success: bool, result_status: String, reason: String) -> void:
	await _request("/engine/v1/adapter/commands/ack", {
		"request_id": _next_request_id("ack"),
		"command_id": command_id,
		"result": {"success": success, "status": result_status, "reason": reason, "adapter_id": ADAPTER_ID}
	})

func _interact() -> void:
	var result := await _request("/engine/v1/simulation/interact", {
		"request_id": _next_request_id("talk"),
		"npc_id": NPC_ID,
		"player_id": PLAYER_ID,
		"interaction": "talk"
	})
	if not result.is_empty():
		status_text = "You spoke with " + str(result.get("response_context", {}).get("name", resident.display_name)) + ". Relationship and memory saved by Engine Host."

func _exit_tree() -> void:
	if token.length() >= 32 and session_id != "":
		_request("/engine/v1/close", {"request_id": _next_request_id("close"), "session_id": session_id})
