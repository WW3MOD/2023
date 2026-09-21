-- WHICH BRANCH THIS DIRECTORY IS BUILT FOR. Read by defcon-banner-combine-lib.lua, which
-- asserts the RULE in both arms and this DECLARATION on top of it: without the declaration an
-- arm whose staging drifted would quietly measure the other branch and still report PASS.
--
-- `expectCombines` is Test.DefconEdgesCombine's answer, not a banner count. Counting banners was
-- tried and is unsound in an autotest -- see the lib header on Ui.Timestep.
BannerArm = { name = "combined", expectCombines = "yes" }
