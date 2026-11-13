derelict-title = Derelict
derelict-description = A salvage crew of up to four strips abandoned stations for the Company. Meet the quota. Leave before the place wakes up.

department-DerelictCrew = Salvage Crew
department-DerelictCrew-description = Contract salvagers working the tug.
job-name-derelict-salvager = Salvager
job-description-derelict-salvager = Board derelicts, haul back anything that sells, and keep your crewmates alive. Sell scrap on the tug's selling pallets, buy gear at the cargo console, and use the navigation uplink on the bridge to pick the next site.
job-supervisors-derelict = the Company, and nobody else

derelict-announcer = Tug Autopilot

derelict-site-unknown = unknown site
derelict-site-dropship = Wrecked Dropship
derelict-site-whiteship = Drifting Survey Ship
derelict-site-outpost = Abandoned Outpost
derelict-site-hydro = Hydroponics Outpost
derelict-site-aisat = Silent AI Satellite
derelict-site-prison = Ruined Prison Ship
derelict-site-interchange = Atmospherics Interchange

derelict-announce-start = Contract open. Quota: {$quota} spesos within {$drops} drops. Pick a site at the navigation uplink on the bridge. Sell salvage on the selling pallets in the main corridor.
derelict-announce-launch = Jump plotted to {$site} (hazard {$hazard}). Drop {$drop} of {$drops}. Brace for FTL.
derelict-announce-arrive = Arrived at {$site}. The tug holds position for {$minutes} minutes. The longer you stay, the worse it gets.
derelict-announce-escalation = Motion detected inside the derelict.
derelict-announce-power = The derelict's power grid just failed.
derelict-announce-hull = The derelict's hull is breaking up. Expect decompression.
derelict-announce-three-minutes = Three minutes to automatic departure.
derelict-announce-one-minute = One minute to automatic departure. Get aboard.
derelict-announce-autodepart = Holding window closed. Departing now.
derelict-announce-depart = Departure in {$seconds} seconds. Anyone not aboard is left behind.
derelict-announce-wipe = No crew life signs. Autopilot is recalling the tug.
derelict-announce-home = Back in deep space. Sold this cycle: {$earned} of {$quota} spesos. Drops left: {$drops}. { $replaced ->
        [0] All hands accounted for.
       *[other] {$replaced} replacement crew issued; the fee has been deducted.
    }
derelict-announce-quota-due = No drops left this cycle. Sold so far: {$earned} of {$quota} spesos. Sell what you have and submit the quota at the navigation uplink within {$minutes} minutes. { $replaced ->
        [0] All hands accounted for.
       *[other] {$replaced} replacement crew issued; the fee has been deducted.
    }
derelict-announce-quota-met = Quota of {$old} spesos met. New quota: {$quota} spesos within {$drops} drops.
derelict-announce-quota-failed = Quota missed: {$earned} of {$quota} spesos. The Company has terminated your contract.
derelict-announce-recorder = Flight recorder received. The Company thanks you for your discretion.

derelict-nav-verb-launch = Jump to {$site} (hazard {$hazard}, {$minutes} min)
derelict-nav-verb-depart = Depart now (leaves everyone not aboard)
derelict-nav-verb-quota = Submit quota ({$earned} / {$quota})
derelict-nav-fail-phase = The tug cannot jump right now.
derelict-nav-fail-site = No route to that site.
derelict-nav-fail-ftl = The FTL drive is not ready.

derelict-status-quota = [bold]Cycle {$cycle}[/bold]: {$earned} / {$quota} spesos sold
derelict-status-drops = Drops left this cycle: {$left} of {$drops}
derelict-status-orbit = Holding in deep space. Awaiting a destination.
derelict-status-jumping = In transit to {$site}.
derelict-status-onsite = On site at {$site}. Automatic departure in [color=#d8452f]{$time}[/color].
derelict-status-leaving = Returning to deep space.
derelict-status-quota-due = [color=#d8452f]Quota is due.[/color] Automatic submission in {$time}.
derelict-status-finished = Contract terminated.

derelict-round-end-failed = [color=#d8452f]The crew missed their quota and the contract was terminated.[/color]
derelict-round-end-ended = The contract was still running when the round ended.
derelict-round-end-stats = Quotas met: {$cycles}. Drops flown: {$drops}. Total sold: {$earned} spesos. Crew replaced: {$lost}. Flight recorders recovered: {$recorders}.

cmd-derelict-desc = Controls the Derelict game mode.
cmd-derelict-help = Usage: derelict status | sites | threats | launch <siteId> | depart | submit | skiptime <seconds>
