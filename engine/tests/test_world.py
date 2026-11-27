import unittest

from engine.contracts import ActorObservation, Vector3, WorldObservation
from engine.world import WorldEngine


class WorldEngineTests(unittest.TestCase):
    def test_fixed_step_clock_is_deterministic(self):
        engine = WorldEngine(fixed_step_seconds=0.5)
        self.assertEqual(engine.advance(1.2), 2)
        self.assertEqual(engine.state.tick, 2)
        self.assertAlmostEqual(engine.state.simulation_time, 1.0)

    def test_observation_updates_game_neutral_actor_state(self):
        engine = WorldEngine()
        observation = WorldObservation(
            adapter_id="fake.adapter",
            game_session_id="session-1",
            observed_at=10.0,
            player_actor_id="player",
            actors=(
                ActorObservation(
                    actor_id="player",
                    position=Vector3(1.0, 2.0, 3.0),
                    is_player=True,
                ),
            ),
        )
        engine.ingest(observation)
        actor = engine.state.actors["player"]
        self.assertEqual((actor.x, actor.y, actor.z), (1.0, 2.0, 3.0))
        self.assertTrue(actor.is_player)

    def test_rejects_time_travel_observation(self):
        engine = WorldEngine()
        first = WorldObservation("fake", "s", 10.0, "p")
        older = WorldObservation("fake", "s", 9.0, "p")
        engine.ingest(first)
        with self.assertRaises(ValueError):
            engine.ingest(older)

    def test_rejects_negative_elapsed_time(self):
        engine = WorldEngine()
        with self.assertRaises(ValueError):
            engine.advance(-1.0)


if __name__ == "__main__":
    unittest.main()
