extends Node2D
class_name LifeResident

var display_name := "Minh An"
var current_activity := "idle"
var moving := false

func _draw() -> void:
	draw_circle(Vector2.ZERO, 15.0, Color(0.15, 0.55, 0.95))
	draw_circle(Vector2(0, -19), 8.0, Color(0.95, 0.78, 0.62))
	draw_line(Vector2(-8, 8), Vector2(-13, 24), Color.WHITE, 3.0)
	draw_line(Vector2(8, 8), Vector2(13, 24), Color.WHITE, 3.0)

func move_to(destination: Vector2, activity: String) -> void:
	current_activity = activity
	moving = true
	var distance := global_position.distance_to(destination)
	var duration := clampf(distance / 150.0, 0.25, 5.0)
	var tween := create_tween()
	tween.tween_property(self, "global_position", destination, duration)
	await tween.finished
	moving = false
