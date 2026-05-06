
- The drones (that are spawned by DR) should be deprioritized when selecting. 

- Medics, for example, have a problem when ordering a group of soldiers to attack move, the medics cant fire so they jsut keep running towards the enemy and end up getting killed and not being useful.
    - One idea is to deprioritize them when selecting, and then add a trait to them that they auto follow adjacent units somehow. This could work well as medics will stay in the rear and be ready to autotarget soldiers as they become hurt, and then return to their default idle behaviour that is to follow. This follow behaviour might be something we want to use for other units too, maybe even add it as a "stance" selector in the bottom bar, and just default medics to have it active at start but we can toggle it for any unit if we want, maybe something like that. Open to other ideas.

- When I gave three queued orders to three units, and then used shift-G (spread/scatter) they ended up only going to two of those points.

- When units move over tiles with a lower move speed/mobility according to their "locomotor", they become slow from what seems like the point when they are in the center of the "fast" tile, all the way to when they reach the cetner of the "slow" tile and then they speed up again. So the problem with this is that they start slowing down before they even enter that tile, and sped up before they have left it. We should slow them down the moment they enter the slow tile, and speed up when they exit it.

- We should be able to target the Supply Route, and it should be the default order for any unit with a weapon, and that order means Go to the flag and stay there until it is fully captured/ceutralized, so that we can order an "attack" on the SR and then queue up other orders after it is captured.

- Units that are auto resupplying/evacuating are sometimes not showing the waypoint line towards their target. Maybe it happens only when they are on auto and targeting a supplytruck or something, not sure. And by the way I think they try to follow empty trucks now, not sure but check this. They should only target trucks with supply left, and if that changes (the truck runs out of supply during) they should stop and switch target

- Medics on "defensive" stance should still "hunt" for hurt soldiers to heal, but at a reduced range, maybe 5 tiles. The problem arises from the limitation that hunt is very long range, and medics might end up running across the battlefield and getting killed. We want to be able to differentiate between long (hunt), short (defensive) and hold position completely. This is how we have designed the new settings for all units basically, but the defensive stance doesnt seem to make any units move at all. For regular soldiers defensive stance means they can reposition if they need to in order to aquire a targeting solution (LoS), but they should not follow the enemy, instead they jsut moves perhaps a little bit to not be blocked, fire and then return to their original position. Hunt means go after enemies, do no return to the original position. 
- Healing soldiers should be limited to one medic per soldier, so we cant heal faster by adding more and more medics. We also need to make the autotargeting for medics to take this into account. If one soldier is hurt, and we have multiple medics, only the closest one should go there, the other ones should remain in place. So when a soldier is targeted it should be deprioritized somehow by the autotargeter. 

- The Flame troopers burst should be less scattered, so the shots in the burst lands closer together, make it half of the current scatter/spread but the same accuracy otherwise

