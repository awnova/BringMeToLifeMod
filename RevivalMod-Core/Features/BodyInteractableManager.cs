using System;
using System.Collections.Generic;
using EFT;
using UnityEngine;
using KeepMeAlive.Components;

namespace KeepMeAlive.Features
{
	// Centralizes body-interactable collider state and picker lifecycle cleanup.
	internal static class BodyInteractableManager
	{
		private static readonly Dictionary<string, BodyInteractable> Cache = new Dictionary<string, BodyInteractable>();

		private static readonly EBodyPart[] TrackedBodyParts =
		{
			EBodyPart.Head,
			EBodyPart.Chest,
			EBodyPart.Stomach,
			EBodyPart.LeftArm,
			EBodyPart.RightArm,
			EBodyPart.LeftLeg,
			EBodyPart.RightLeg
		};

		public static void Tick(Player player)
		{
			if (player?.HealthController == null || player.IsAI)
			{
				return;
			}

			try
			{
				bool isCritical = RMSession.IsPlayerCritical(player.ProfileId);
				var state = RMSession.GetPlayerState(player.ProfileId);
				bool isRevived = state?.State == RMState.Revived;

				bool isInjured = false;
				if (!isCritical && !isRevived)
				{
					for (int i = 0; i < TrackedBodyParts.Length; i++)
					{
						var hp = player.HealthController.GetBodyPartHealth(TrackedBodyParts[i]);
						if (hp.Current >= hp.Maximum)
						{
							continue;
						}

						isInjured = true;
						break;
					}
				}

				bool shouldEnable = isCritical || isRevived || isInjured;

				if (!Cache.TryGetValue(player.ProfileId, out var interactable) || interactable == null)
				{
					foreach (var found in player.GetComponentsInChildren<BodyInteractable>(true))
					{
						if (found.Revivee?.ProfileId == player.ProfileId)
						{
							Cache[player.ProfileId] = interactable = found;
							break;
						}
					}
				}

				if (interactable == null)
				{
					return;
				}

				bool canEnable = shouldEnable && !interactable.HasActivePicker;
				var colliders = interactable.GetComponents<Collider>();

				for (int i = 0; i < colliders.Length; i++)
				{
					var col = colliders[i];
					if (col != null && col.enabled != canEnable)
					{
						col.enabled = canEnable;
					}
				}
			}
			catch (Exception ex)
			{
				Plugin.LogSource.LogError($"[BodyInteractableManager] Tick error: {ex.Message}");
			}
		}

		public static void Remove(string playerId)
		{
			if (string.IsNullOrEmpty(playerId))
			{
				return;
			}

			Cache.Remove(playerId);
		}

		public static void ForceClosePicker(string playerId)
		{
			if (string.IsNullOrEmpty(playerId))
			{
				return;
			}

			try
			{
				if (Cache.TryGetValue(playerId, out var interactable) && interactable != null)
				{
					interactable.ForceClosePicker();
				}
			}
			catch (Exception ex)
			{
				Plugin.LogSource.LogWarning($"[BodyInteractableManager] ForceClosePicker error: {ex.Message}");
			}
		}
	}
}
