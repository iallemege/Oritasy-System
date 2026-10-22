# -*- coding: utf-8 -*-
"""Radio variants. Complete air-force / AAA sentences. Originals stay in AceRadioChatter."""
from __future__ import print_function
import os
import random

random.seed(14403)
OUT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                    "..", "src", "AceRadioBank.cs"))
BAD_ZH = [
    u"反复横跳", u"编队", u"队形", u"左翼", u"右翼", u"僚机", u"横跳",
    u"影子", u"老手", u"新人", u"这人", u"那人", u"这家伙", u"那家伙",
    u"那架飞机", u"天上那位", u"雷达上那个", u"你说呢", u"我困了",
    u"真的假的", u"冲老手", u"那目标", u"刚才那人",
]
BAD_EN = [
    "formation", "wingman", "form-up", "form up", "your wing", "my wing",
    "keep it tight", "third hop", "serial hop", "that guy", "this one",
    "the new guy", "the veteran", "that shadow", "that jet",
]


def P(z, e):
    return (z, e)


def bad(z, e):
    for w in BAD_ZH:
        if w in z:
            return True
    el = e.lower()
    for w in BAD_EN:
        if w in el:
            return True
    return False


def fill(n, heads, mids, tails):
    seen = set()
    out = []
    combos = []
    for h in heads:
        for m in mids:
            for t in tails:
                combos.append((h, m, t))
    random.shuffle(combos)
    for h, m, t in combos:
        if len(out) >= n:
            break
        zh = h[0] + m[0] + t[0]
        en = h[1] + m[1] + t[1]
        if bad(zh, en) or '"' in zh or '"' in en or "\\" in zh or "\\" in en:
            continue
        if zh in seen:
            continue
        seen.add(zh)
        out.append((zh, en))
    if len(out) < n:
        raise SystemExit("need %d got %d head=%s" % (n, len(out), heads[0][0]))
    return out


# Runtime: {P}=player nick (红蝎子/三条线), {L}=map place, {0}=squadron or airfield.
LOC = [
    P(u"，{L}", ", {L}"),
    P(u"，位置{L}", ", position {L}"),
    P(u"，{L}方向", ", bearing {L}"),
    P(u"，位于{L}", ", located {L}"),
]
LOC_TGT = LOC + [
    P(u"，目标在{L}", ", target at {L}"),
    P(u"，{P}在{L}", ", {P} at {L}"),
]
LOC_FIRE = [
    P(u"，目标{P}", ", target {P}"),
    P(u"，{P}在{L}", ", {P} at {L}"),
    P(u"，{L}方向", ", bearing {L}"),
    P(u"，攻击{L}方向", ", attacking {L}"),
    P(u"，目标在{L}", ", target at {L}"),
]
LOC_GND = [
    P(u"，{L}", ", {L}"),
    P(u"，{L}方向", ", bearing {L}"),
    P(u"，目标在{L}", ", target at {L}"),
    P(u"，空中目标在{L}", ", airborne target at {L}"),
]
LOC_IDLE = [
    P(u"，{L}", ", {L}"),
    P(u"，在{L}", ", at {L}"),
    P(u"，{L}方向", ", bearing {L}"),
    P(u"", ""),
]
END = [
    P(u"。", "."),
    P(u"，完毕。", ", out."),
]
END_AIR = END + [
    P(u"，保持观察。", ", maintaining visual."),
    P(u"，通报全队。", ", all aircraft."),
]
END_FIGHT = [
    P(u"。", "."),
    P(u"，完毕。", ", out."),
    P(u"，注意观察。", ", watch the target."),
]


def main():
    bank = {}
    bank["air.contact"] = fill(350, [
        P(u"发现目标", "Contact"),
        P(u"雷达接触", "Radar contact"),
        P(u"目视确认", "Tally"),
        P(u"发现空中目标", "Airborne contact"),
        P(u"雷达发现目标", "Radar contact, hostile"),
        P(u"目视接触", "Visual contact"),
        P(u"确认空中目标", "Confirm airborne target"),
        P(u"发现敌情", "Hostile contact"),
        P(u"空中接触", "Air contact"),
        P(u"雷达截获目标", "Radar lock-on contact"),
        P(u"发现{P}", "Contact {P}"),
        P(u"目视{P}", "Tally {P}"),
        P(u"雷达接触{P}", "Radar contact {P}"),
        P(u"确认目标", "Confirm target"),
    ], LOC_TGT, END_AIR + [
        P(u"，准备接敌。", ", preparing to engage."),
        P(u"，准备交战。", ", engaging."),
        P(u"，继续跟踪。", ", continuing track."),
        P(u"，高度未知。", ", altitude unknown."),
    ])
    bank["air.fox"] = fill(320, [
        P(u"导弹发射", "Fox two"),
        P(u"已发射", "Missile away"),
        P(u"武器投放", "Weapons release"),
        P(u"锁定发射", "Locked, firing"),
        P(u"对目标开火", "Firing on target"),
        P(u"对{P}开火", "Firing on {P}"),
        P(u"导弹已出筒", "Missile is away"),
        P(u"攻击目标", "Engaging target"),
        P(u"对{P}发射", "Fox, {P}"),
        P(u"武器发射", "Weapons away"),
        P(u"开始攻击", "Commencing attack"),
        P(u"占位发射", "In position, firing"),
    ], LOC_FIRE, END_FIGHT + [
        P(u"，保持锁定。", ", maintaining lock."),
        P(u"，观察弹道。", ", watching the shot."),
        P(u"，脱离准备。", ", preparing to extend."),
    ])
    bank["air.incoming"] = fill(280, [
        P(u"注意来弹", "Break, missile inbound"),
        P(u"来弹", "Missile inbound"),
        P(u"对方开火", "Hostile is firing"),
        P(u"我被锁定", "I am spiked"),
        P(u"机动规避", "Breaking"),
        P(u"{P}开火", "{P} is firing"),
        P(u"注意导弹", "Missile warning"),
        P(u"干扰弹投放", "Flares out"),
        P(u"来弹，机动", "Inbound, breaking"),
        P(u"对方已发射", "Hostile missile away"),
    ], LOC_TGT, END_FIGHT + [
        P(u"，立即机动。", ", break now."),
        P(u"，投放干扰。", ", deploying countermeasures."),
        P(u"，不要直线。", ", do not hold heading."),
    ])
    bank["air.onsix"] = fill(240, [
        P(u"目标在我后方", "Target on my six"),
        P(u"{P}在我后方", "{P} on my six"),
        P(u"我机六点钟有目标", "Bandit at six o'clock"),
        P(u"无法摆脱目标", "Cannot shake the target"),
        P(u"请求支援", "Request support"),
        P(u"被咬尾", "I'm being tailed"),
        P(u"后方有目标", "Target astern"),
        P(u"六点钟被占位", "Six o'clock occupied"),
        P(u"目标贴在后方", "Target is glued aft"),
        P(u"请求把目标驱离", "Request you drive the target off"),
    ], LOC_TGT, END_FIGHT + [
        P(u"，请求支援。", ", request support."),
        P(u"，我在机动。", ", I am maneuvering."),
    ])
    bank["air.adv"] = fill(240, [
        P(u"已咬住目标", "I've got the target"),
        P(u"已占位", "In position"),
        P(u"目标无法脱离", "Target cannot extend"),
        P(u"瞄准目标", "Lined up on target"),
        P(u"已咬住{P}", "I've got {P}"),
        P(u"准备开火", "Preparing to fire"),
        P(u"进入射击位置", "Entering firing position"),
        P(u"目标在我机前方", "Target is ahead"),
        P(u"角度有效", "Angle is good"),
        P(u"跟踪稳定", "Track is stable"),
    ], LOC_FIRE, END_FIGHT + [
        P(u"，准备发射。", ", preparing to fire."),
        P(u"，保持占位。", ", holding position."),
    ])
    bank["air.hit"] = fill(240, [
        P(u"我机中弹", "I'm hit"),
        P(u"中弹", "Hit, hit"),
        P(u"我机受损", "Damage, I'm trailing"),
        P(u"被命中", "That one landed"),
        P(u"{P}命中我机", "{P} hit me"),
        P(u"控制系统受损", "Controls damaged"),
        P(u"我机冒烟", "I'm trailing smoke"),
        P(u"结构受损", "Airframe damaged"),
        P(u"仍可操纵", "Still controllable"),
        P(u"动力受损", "Power damaged"),
    ], LOC_TGT, END_FIGHT + [
        P(u"，评估损伤。", ", assessing damage."),
        P(u"，仍可飞行。", ", still flying."),
    ])
    bank["air.down"] = fill(220, [
        P(u"弹射！弹射！弹射！", "Eject! Eject! Eject!"),
        P(u"MAYDAY！MAYDAY！MAYDAY！", "MAYDAY! MAYDAY! MAYDAY!"),
        P(u"弹射！弹射！弹射！我机失控", "Eject! Eject! Eject! Going down"),
        P(u"MAYDAY！MAYDAY！MAYDAY！我机失控", "MAYDAY! MAYDAY! MAYDAY! Going down"),
        P(u"弹射！弹射！弹射！无法保持高度", "Eject! Eject! Eject! Cannot hold altitude"),
        P(u"MAYDAY！MAYDAY！MAYDAY！无法保持高度", "MAYDAY! MAYDAY! MAYDAY! Cannot hold altitude"),
        P(u"弹射！弹射！弹射！我机坠落", "Eject! Eject! Eject! Going in"),
        P(u"MAYDAY！MAYDAY！MAYDAY！我机坠落", "MAYDAY! MAYDAY! MAYDAY! Going in"),
        P(u"弹射！弹射！弹射！操纵失效", "Eject! Eject! Eject! Controls gone"),
        P(u"MAYDAY！MAYDAY！MAYDAY！操纵失效", "MAYDAY! MAYDAY! MAYDAY! Controls gone"),
        P(u"弹射！弹射！弹射！无法返航", "Eject! Eject! Eject! Cannot return to base"),
        P(u"MAYDAY！MAYDAY！MAYDAY！无法返航", "MAYDAY! MAYDAY! MAYDAY! Cannot return to base"),
        P(u"弹射！弹射！弹射！{P}击落我机", "Eject! Eject! Eject! {P} splashed me"),
        P(u"MAYDAY！MAYDAY！MAYDAY！{P}击落我机", "MAYDAY! MAYDAY! MAYDAY! {P} splashed me"),
        P(u"弹射！弹射！弹射！高度持续下降", "Eject! Eject! Eject! Altitude decreasing"),
        P(u"MAYDAY！MAYDAY！MAYDAY！高度持续下降", "MAYDAY! MAYDAY! MAYDAY! Altitude decreasing"),
        P(u"弹射！弹射！弹射！放弃飞机", "Eject! Eject! Eject! Abandoning aircraft"),
        P(u"MAYDAY！MAYDAY！MAYDAY！放弃飞机", "MAYDAY! MAYDAY! MAYDAY! Abandoning aircraft"),
        P(u"弹射！弹射！弹射！无法恢复", "Eject! Eject! Eject! Cannot recover"),
        P(u"MAYDAY！MAYDAY！MAYDAY！无法恢复", "MAYDAY! MAYDAY! MAYDAY! Cannot recover"),
    ], [
        P(u"", ""),
        P(u"在{L}！", " at {L}!"),
        P(u"位置{L}！", " position {L}!"),
        P(u"{L}方向！", " bearing {L}!"),
        P(u"位于{L}！", " located {L}!"),
        P(u"目标在{L}！", " target at {L}!"),
        P(u"{P}在{L}！", " {P} at {L}!"),
        P(u"，{L}！", ", {L}!"),
        P(u"，位置{L}！", ", position {L}!"),
        P(u"，{L}方向！", ", bearing {L}!"),
        P(u"！", "!"),
    ], [
        P(u"", ""),
    ])
    bank["air.ace"] = fill(160, [
        P(u"压制不住目标", "Cannot suppress the target"),
        P(u"无法命中目标", "Cannot hit the target"),
        P(u"目标机动过强", "Target maneuver is too strong"),
        P(u"拦不住{P}", "Cannot stop {P}"),
        P(u"该目标不是普通飞机", "That target is not ordinary"),
        P(u"连续丢失目标", "Repeatedly losing the target"),
        P(u"{P}无法拦截", "Cannot intercept {P}"),
        P(u"射击无效", "Shots ineffective"),
    ], LOC_TGT, END_FIGHT + [
        P(u"，请求支援。", ", request support."),
        P(u"，不要恋战。", ", do not stay in the fight."),
    ])
    bank["air.idle"] = fill(400, [
        P(u"油量正常", "Fuel state good"),
        P(u"航向保持", "Holding heading"),
        P(u"本空域暂无目标", "No contacts this sector"),
        P(u"继续巡逻", "Continuing patrol"),
        P(u"雷达暂无接触", "Radar is clear"),
        P(u"高度正常", "Altitude is good"),
        P(u"收到", "Copy"),
        P(u"继续值守", "Still on station"),
        P(u"保持警戒", "Maintaining watch"),
        P(u"空情正常", "Air picture is quiet"),
        P(u"通讯正常", "Comms check, loud and clear"),
        P(u"仪表正常", "Instruments normal"),
        P(u"继续观察", "Continuing visual scan"),
        P(u"本机状态正常", "Aircraft status normal"),
    ], LOC_IDLE, END_AIR + [
        P(u"，保持航向。", ", hold heading."),
        P(u"，注意空情。", ", watch the air picture."),
        P(u"，等待指令。", ", standing by."),
        P(u"，继续扫描。", ", continuing scan."),
    ])
    bank["air.rumor"] = fill(150, [
        P(u"指挥通报", "Command"),
        P(u"各机注意", "All aircraft"),
        P(u"指挥网通报", "Command net"),
        P(u"注意", "Be advised"),
        P(u"空情通报", "Air picture update"),
        P(u"各单位注意", "All stations"),
    ], [
        P(u"，{0}可能进入{L}", ", {0} may enter {L}"),
        P(u"，{0}可能在本空域出现", ", {0} may appear this sector"),
        P(u"，{0}有进入迹象", ", {0} may be inbound"),
        P(u"，前方可能有{0}活动", ", possible {0} activity ahead"),
        P(u"，{0}可能接近{L}", ", {0} may approach {L}"),
        P(u"，如{0}进入本空域", ", if {0} enters this sector"),
    ], [
        P(u"，保持警戒。", ", stay sharp."),
        P(u"，不要恋战。", ", do not press the attack."),
        P(u"，准备脱离。", ", prepare to abort."),
        P(u"，加强观察。", ", tighten the watch."),
        P(u"。", "."),
    ])
    bank["air.traitor"] = fill(160, [
        P(u"发现叛变目标", "Traitor contact"),
        P(u"识别码已变，按敌机处置", "IFF flipped, treat as hostile"),
        P(u"叛变目标，自由开火", "Defector, weapons free"),
        P(u"不要放过叛变目标", "Do not let the defector go"),
        P(u"击落叛变目标", "Splash the defector"),
        P(u"前友军已叛变", "Former friendly has defected"),
        P(u"该目标已投敌", "That target has defected"),
        P(u"不要接叛变目标呼叫", "Do not answer the defector"),
    ], LOC_GND + [
        P(u"，{L}方向", ", bearing {L}"),
        P(u"，目标在{L}", ", target at {L}"),
    ], END_FIGHT + [
        P(u"，立即拦截。", ", intercept now."),
        P(u"，按敌情处理。", ", handle as hostile."),
    ])
    bank["air.repeat"] = fill(120, [
        P(u"叛变目标再次换边", "Defector switched again"),
        P(u"该目标第二次叛变", "Second defection"),
        P(u"仍按敌机开火", "Still weapons free"),
        P(u"不要手软，击落该目标", "No warning, splash the target"),
        P(u"两面派目标，开火", "Two-timer, fire"),
        P(u"再次识别翻转，按敌处置", "IFF flipped again, hostile"),
    ], LOC_GND, END_FIGHT + [
        P(u"，不要警告。", ", no warning shots."),
        P(u"，立即击落。", ", splash now."),
    ])
    bank["air.bitter"] = fill(120, [
        P(u"归队目标不可靠", "Returnee is untrusted"),
        P(u"识别是友，不要靠近", "Friendly IFF, do not close"),
        P(u"不要把任务交给归队目标", "Do not task the returnee"),
        P(u"保持间隔，该目标叛过", "Hold interval, that target defected"),
        P(u"油量正常，与归队目标保持距离", "Fuel good, stay off the returnee"),
        P(u"指挥禁止单独任务", "Command: no solo tasking"),
    ], LOC_IDLE + [
        P(u"，{L}", ", {L}"),
        P(u"，在{L}", ", at {L}"),
    ], END + [
        P(u"，继续观察。", ", keep watching."),
        P(u"，不要掩护。", ", do not cover."),
        P(u"，保持间隔。", ", hold interval."),
    ])

    bank["gnd.contact"] = fill(80, [
        P(u"发现空中目标", "Enemy aircraft inbound"),
        P(u"目视确认空中目标", "Visual on the aircraft"),
        P(u"空袭警报", "Air raid, air raid"),
        P(u"发现飞机", "Aircraft spotted"),
        P(u"对空监视发现目标", "Air watch has a contact"),
        P(u"空中目标进入防区", "Aircraft entering the sector"),
        P(u"发现{P}", "Contact {P}"),
        P(u"雷达发现空中目标", "Radar contact, aircraft"),
    ], LOC_GND, END + [
        P(u"，防空准备。", ", air defense ready."),
        P(u"，立即通报。", ", report now."),
        P(u"，保持跟踪。", ", keep tracking."),
    ])
    bank["gnd.fox"] = fill(70, [
        P(u"对空开火", "Firing"),
        P(u"高炮射击", "AAA engaging"),
        P(u"对空导弹发射", "Launching SAMs"),
        P(u"拦截空中目标", "Engaging the aircraft"),
        P(u"对空武器发射", "Missiles away"),
        P(u"防空开火", "Air defense firing"),
        P(u"射击空中目标", "Engaging airborne target"),
        P(u"对{P}射击", "Firing on {P}"),
    ], LOC_GND + [
        P(u"，目标{P}", ", target {P}"),
        P(u"，{P}在{L}", ", {P} at {L}"),
    ], END + [
        P(u"，观察弹着。", ", watch impact."),
        P(u"，继续射击。", ", continue fire."),
    ])
    bank["gnd.incoming"] = fill(80, [
        P(u"阵地遇袭", "We're under attack"),
        P(u"车队遇袭", "Convoy under attack"),
        P(u"隐蔽", "Take cover"),
        P(u"空中打击来了", "Incoming air strike"),
        P(u"正在遭受空袭", "Air raid in progress"),
        P(u"{P}在攻击阵地", "{P} is attacking the position"),
        P(u"不要暴露", "Do not expose"),
        P(u"火力覆盖过来了", "We are taking fire"),
    ], LOC_GND, END + [
        P(u"，立即隐蔽。", ", take cover now."),
        P(u"，疏散。", ", disperse."),
        P(u"，防空还击。", ", AAA return fire."),
    ])
    bank["gnd.strafe"] = fill(70, [
        P(u"敌机超低空来袭", "They're coming in low"),
        P(u"要扫射了，散开", "Gun run, break up"),
        P(u"空中目标对准阵地", "Aircraft lining up on us"),
        P(u"离开道路", "Get off the road"),
        P(u"低空目标进入", "Low-level target inbound"),
        P(u"{P}压下来了", "{P} is pressing in"),
        P(u"超低空，注意射击", "Low level, engage"),
    ], LOC_GND, END + [
        P(u"，散开。", ", break up."),
        P(u"，对空射击。", ", fire."),
        P(u"，不要集中。", ", do not bunch up."),
    ])
    bank["gnd.track"] = fill(70, [
        P(u"跟踪目标", "Tracking the target"),
        P(u"锁定空中目标", "I've got a lock"),
        P(u"不要让目标脱离", "Don't let the target extend"),
        P(u"高炮准备开火", "AAA, fire"),
        P(u"持续跟踪{P}", "Tracking {P}"),
        P(u"目标进入射程", "Target in range"),
        P(u"瞄准空中目标", "Aiming at the aircraft"),
    ], LOC_GND, END + [
        P(u"，准备射击。", ", prepare to fire."),
        P(u"，保持锁定。", ", hold lock."),
        P(u"，通报方位。", ", call the bearing."),
    ])
    bank["gnd.hit"] = fill(70, [
        P(u"阵地被命中", "We've been hit"),
        P(u"直接命中", "Direct hit"),
        P(u"装甲被打穿", "Armor penetrated"),
        P(u"车辆受损", "Vehicle damaged"),
        P(u"{P}命中阵地", "{P} hit the position"),
        P(u"起火", "Fire on the vehicle"),
        P(u"火力命中我方", "We are taking hits"),
    ], LOC_GND, END + [
        P(u"，抢救。", ", damage control."),
        P(u"，人员撤离。", ", get the crew off."),
        P(u"，继续还击。", ", keep returning fire."),
    ])
    bank["gnd.down"] = fill(60, [
        P(u"车辆失去机动", "Vehicle is finished"),
        P(u"弃车", "Abandon the vehicle"),
        P(u"阵地失守", "Position is lost"),
        P(u"车队失去战斗力", "The column is gone"),
        P(u"无法继续作战", "Cannot continue"),
        P(u"{P}摧毁了车辆", "{P} wrecked the vehicle"),
        P(u"装备报废", "Equipment is gone"),
    ], LOC_GND, END + [
        P(u"，人员撤离。", ", crew out."),
        P(u"，放弃阵地。", ", abandon the position."),
        P(u"，重复，失去机动。", ", repeat, mobility lost."),
    ])
    bank["gnd.ace"] = fill(50, [
        P(u"装甲单位被连续摧毁", "Armor is being wiped out"),
        P(u"坦克失去战斗力", "The tanks are gone"),
        P(u"拦不住空中目标", "We cannot stop the aircraft"),
        P(u"地面部队损失过大", "Ground units are being torn apart"),
        P(u"压制不住{P}", "Cannot suppress {P}"),
        P(u"防空无效", "Air defense ineffective"),
    ], LOC_GND, END + [
        P(u"，请求支援。", ", request support."),
        P(u"，停止暴露。", ", stop exposing."),
        P(u"，立即隐蔽。", ", take cover now."),
    ])
    bank["gnd.traitor"] = fill(50, [
        P(u"发现叛变飞机，防空准备", "Traitor aircraft, AAA up"),
        P(u"叛变目标，开火", "That's the defector, fire"),
        P(u"指挥部，叛变目标进入防区", "HQ, traitor in our sector"),
        P(u"不要让叛变目标离开机场", "Don't let the defector leave the field"),
        P(u"按敌机对空射击", "Engage as hostile"),
        P(u"识别已变，对空开火", "IFF changed, fire"),
    ], LOC_GND, END + [
        P(u"，立即射击。", ", fire now."),
        P(u"，通报指挥。", ", report to command."),
    ])
    bank["gnd.repeat"] = fill(40, [
        P(u"叛变目标再次换边，开火", "Defector switched again, fire"),
        P(u"防空注意，两面派目标", "AAA, that's the two-timer"),
        P(u"指挥部，该目标仍按敌机打", "HQ, still a hostile"),
        P(u"第二次叛变，不要警告", "Second time, no warning"),
        P(u"对空自由开火", "Weapons free, airborne"),
    ], LOC_GND, END + [
        P(u"。", "."),
        P(u"，立即击落。", ", splash now."),
        P(u"，不要停火。", ", do not check fire."),
    ])
    bank["gnd.bitter"] = fill(40, [
        P(u"归队目标在头顶，不要敬礼", "Returnee overhead, do not salute"),
        P(u"先前叛变的目标，炮口不要松", "That's the former defector, stay on guns"),
        P(u"指挥：识别是友，飞行员不可靠", "HQ: friendly IFF, untrusted pilot"),
        P(u"不要靠近归队目标", "Do not close on the returnee"),
        P(u"保持戒备，该目标叛过", "Stay ready, that target defected"),
    ], LOC_IDLE, END + [
        P(u"。", "."),
        P(u"，继续监视。", ", keep watching."),
        P(u"，不要接呼叫。", ", do not answer."),
    ])
    bank["army.first"] = fill(60, [
        P(u"各单位注意", "All stations"),
        P(u"防空网注意", "Air defense"),
        P(u"指挥通报", "Command"),
        P(u"地面监视", "Ground watch"),
        P(u"各连注意", "All batteries"),
        P(u"对空警戒", "Air watch"),
    ], [
        P(u"，我方飞机已从{0}叛变，按敌机处置", ", friendly aircraft defected from {0}, treat as hostile"),
        P(u"，叛徒可能从{0}方向脱离", ", traitor may egress from {0}"),
        P(u"，确认{0}识别码变更", ", confirm IFF change at {0}"),
        P(u"，{0}方向出现叛变目标", ", defector track near {0}"),
        P(u"，叛变飞机离开{0}", ", defector left {0}"),
        P(u"，{0}有识别翻转", ", IFF flip at {0}"),
    ], [
        P(u"。", "."),
        P(u"，立即拦截。", ", intercept now."),
        P(u"，不要接呼叫。", ", do not answer calls."),
        P(u"，防空保持热备。", ", batteries stay hot."),
        P(u"，通报各连。", ", pass to all batteries."),
    ])
    bank["army.net"] = fill(90, [
        P(u"第三营暂停前出", "Third battalion, hold forward movement"),
        P(u"炮兵注意", "Artillery"),
        P(u"把叛变机型通报各连", "Pass the defector type to all batteries"),
        P(u"拦截优先", "Intercept has priority"),
        P(u"地面监视", "Ground watch"),
        P(u"防空连保持热备", "SAM battery stay hot"),
        P(u"补给车队改线", "Resupply column, reroute"),
        P(u"指挥网", "Command net"),
        P(u"各单位", "All stations"),
        P(u"对空警戒不要撤", "Air watch stays up"),
    ], [
        P(u"，先把空情摸清", ", until the air picture is clear"),
        P(u"，{0}方向暂时停火，避免误伤地面", ", check fire toward {0}, avoid hitting our ground"),
        P(u"，不要让叛变目标靠近车队", ", keep the defector off the convoys"),
        P(u"，叛徒已换识别，按新敌情处理", ", traitor IFF has changed, new hostile"),
        P(u"，注意低空接近", ", watch low-level approaches"),
        P(u"，注意{0}方向空中威胁", ", air threat from {0}"),
        P(u"，不要接叛变目标呼叫", ", do not answer the defector"),
        P(u"，{0}方向保持警戒", ", hold watch toward {0}"),
        P(u"，空中目标按敌处置", ", treat airborne target as hostile"),
    ], END + [
        P(u"，完毕。", ", out."),
        P(u"，重复。", ", I say again."),
        P(u"，立即执行。", ", execute now."),
    ])
    bank["army.againFirst"] = fill(40, [
        P(u"各单位", "All stations"),
        P(u"指挥通报", "Command"),
        P(u"防空注意", "Air defense"),
        P(u"确认通报", "Confirm"),
        P(u"各连", "All batteries"),
    ], [
        P(u"，叛徒从{0}再次换边，仍按敌机处置", ", the traitor defected again from {0}, still hostile"),
        P(u"，第二次叛变，对该目标自由开火", ", second defection, weapons free"),
        P(u"，确认{0}识别码再次翻转", ", confirm second IFF flip at {0}"),
        P(u"，{0}方向叛变目标再次出现", ", defector again near {0}"),
        P(u"，不要接该目标呼叫", ", do not answer that target"),
    ], END + [
        P(u"。", "."),
        P(u"，立即拦截。", ", intercept now."),
        P(u"，不要警告。", ", no warning shots."),
        P(u"，防空保持热备。", ", batteries stay hot."),
    ])
    bank["army.againNet"] = fill(60, [
        P(u"两边指挥网均按叛徒处置", "Both nets have the target as traitor"),
        P(u"防空保持热备", "Batteries stay hot"),
        P(u"车队注意拉开间距", "Convoys, stay dispersed"),
        P(u"指挥通报", "Command"),
        P(u"不要接呼叫", "Do not answer"),
        P(u"该目标再次卖出情报", "That target sold us again"),
    ], [
        P(u"，不要接呼叫", ", do not answer"),
        P(u"，{0}方向吵作一团", ", {0} net is busy"),
        P(u"，两面派目标在空中", ", the two-timer is airborne"),
        P(u"，前友军现为敌", ", former friendly is hostile again"),
        P(u"，{0}方向保持警戒", ", hold watch toward {0}"),
        P(u"，按敌机拦截", ", intercept as hostile"),
    ], END + [
        P(u"。", "."),
        P(u"，完毕。", ", out."),
        P(u"，立即执行。", ", execute now."),
        P(u"，重复。", ", I say again."),
    ])
    bank["army.allyFirst"] = fill(30, [
        P(u"指挥", "Command"),
        P(u"各机注意", "All flights"),
        P(u"指挥网", "Command net"),
        P(u"归队通报", "Returnee notice"),
        P(u"各单位", "All stations"),
    ], [
        P(u"，叛变飞行员已从{0}归队，禁止单独任务", ", defector re-joined at {0}, no solo tasking"),
        P(u"，{0}归队目标不可靠，保持间隔", ", returnee at {0} is untrusted, hold interval"),
        P(u"，识别码恢复友军，信任不恢复", ", friendly IFF restored, trust is not"),
        P(u"，不要把任务交给归队目标", ", do not task the returnee"),
        P(u"，{0}方向归队目标只给编号", ", returnee at {0} is number only"),
    ], END + [
        P(u"。", "."),
        P(u"，继续观察。", ", keep watching."),
        P(u"，不要掩护。", ", do not cover."),
    ])
    bank["army.allyNet"] = fill(40, [
        P(u"不要掩护归队目标", "Do not cover the returnee"),
        P(u"车队网注意", "Convoy net"),
        P(u"归队目标脱离不要追", "If the returnee peels off, let it go"),
        P(u"油料可以给，话少说", "Fuel is fine, keep it short"),
        P(u"识别是友，飞行员不可靠", "Friendly paint, untrusted pilot"),
        P(u"不要接归队目标战术呼叫", "Do not take the returnee's tactical calls"),
    ], [
        P(u"，该目标曾经叛变", ", that target defected once"),
        P(u"，冷处理", ", stay cold"),
        P(u"，我们不追叛徒", ", we do not chase a traitor"),
        P(u"，{0}方向保持间隔", ", hold interval toward {0}"),
        P(u"，在{L}保持戒备", ", stay ready at {L}"),
        P(u"，不要靠近", ", do not close"),
    ], END + [
        P(u"。", "."),
        P(u"，完毕。", ", out."),
        P(u"，继续值守。", ", remain on station."),
    ])

    air_keys = ["air.contact", "air.fox", "air.incoming", "air.onsix", "air.adv",
                "air.hit", "air.down", "air.ace", "air.idle", "air.rumor",
                "air.traitor", "air.repeat", "air.bitter"]
    army_keys = ["gnd.contact", "gnd.fox", "gnd.incoming", "gnd.strafe", "gnd.track",
                 "gnd.hit", "gnd.down", "gnd.ace", "gnd.traitor", "gnd.repeat",
                 "gnd.bitter", "army.first", "army.net", "army.againFirst",
                 "army.againNet", "army.allyFirst", "army.allyNet"]
    air_n = sum(len(bank[k]) for k in air_keys)
    army_n = sum(len(bank[k]) for k in army_keys)
    if air_n != 3000:
        raise SystemExit("air %d" % air_n)
    if army_n != 1000:
        raise SystemExit("army %d" % army_n)

    def lines_for(keys, lang):
        idx = 0 if lang == "zh" else 1
        out = []
        for k in keys:
            for pair in bank[k]:
                t = pair[idx].replace("\r", " ").replace("\n", " ")
                out.append("%s|%s" % (k, t))
        return out

    zh = lines_for(air_keys + army_keys, "zh")
    en = lines_for(air_keys + army_keys, "en")

    def chunks(xs, n=800):
        return [xs[i:i + n] for i in range(0, len(xs), n)]

    def verb(chunk):
        return '@"' + "\n".join(chunk).replace('"', '""') + '"'

    zh_cs = [verb(c) for c in chunks(zh)]
    en_cs = [verb(c) for c in chunks(en)]
    parts = [
        "// <auto-generated> radio chat variants. Keep originals in AceRadioChatter.",
        "using System;",
        "using System.Collections.Generic;",
        "using UnityEngine;",
        "",
        "namespace Oritasy",
        "{",
        "    internal static class AceRadioBank",
        "    {",
        "        private static Dictionary<string, string[]> _zh;",
        "        private static Dictionary<string, string[]> _en;",
        "        private static bool _ready;",
        "",
        "        internal static string PickMerged(string key, string[] origEn, string[] origZh)",
        "        {",
        "            Ensure();",
        "            bool zh = UiLang.IsChinese;",
        "            string[] extra = Extra(zh ? _zh : _en, key);",
        "            string[] orig = zh ? origZh : origEn;",
        "            if (orig == null)",
        "                orig = origEn;",
        "            int n0 = orig != null ? orig.Length : 0;",
        "            int n1 = extra != null ? extra.Length : 0;",
        "            if (n0 + n1 <= 0)",
        "                return \"\";",
        "            int i = UnityEngine.Random.Range(0, n0 + n1);",
        "            if (i < n0)",
        "                return orig[i];",
        "            return extra[i - n0];",
        "        }",
        "",
        "        internal static string PickMergedFmt(string key, string[] origEn, string[] origZh, string p0)",
        "        {",
        "            string s = PickMerged(key, origEn, origZh);",
        "            if (string.IsNullOrEmpty(s))",
        "                return \"\";",
        "            if (p0 == null)",
        "                p0 = \"\";",
        "            return s.Replace(\"{0}\", p0);",
        "        }",
        "",
        "        private static string[] Extra(Dictionary<string, string[]> map, string key)",
        "        {",
        "            string[] arr = null;",
        "            if (map == null || string.IsNullOrEmpty(key) || !map.TryGetValue(key, out arr))",
        "                return null;",
        "            return arr;",
        "        }",
        "",
        "        private static void Ensure()",
        "        {",
        "            if (_ready)",
        "                return;",
        "            _ready = true;",
        "            _zh = Parse(ZhAll());",
        "            _en = Parse(EnAll());",
        "        }",
        "",
        "        private static string ZhAll()",
        "        {",
        "            return " + " + \"\\n\" + ".join(["Zh%d" % i for i in range(len(zh_cs))]) + ";",
        "        }",
        "",
        "        private static string EnAll()",
        "        {",
        "            return " + " + \"\\n\" + ".join(["En%d" % i for i in range(len(en_cs))]) + ";",
        "        }",
        "",
        "        private static Dictionary<string, string[]> Parse(string blob)",
        "        {",
        "            Dictionary<string, List<string>> map = new Dictionary<string, List<string>>();",
        "            if (string.IsNullOrEmpty(blob))",
        "                return new Dictionary<string, string[]>();",
        "            string[] lines = blob.Split(new char[] { '\\n' });",
        "            for (int i = 0; i < lines.Length; i++)",
        "            {",
        "                string line = lines[i];",
        "                if (line == null || line.Length == 0)",
        "                    continue;",
        "                if (line[line.Length - 1] == '\\r')",
        "                    line = line.Substring(0, line.Length - 1);",
        "                int bar = line.IndexOf('|');",
        "                if (bar <= 0 || bar >= line.Length - 1)",
        "                    continue;",
        "                string key = line.Substring(0, bar);",
        "                string text = line.Substring(bar + 1);",
        "                List<string> list;",
        "                if (!map.TryGetValue(key, out list))",
        "                {",
        "                    list = new List<string>(64);",
        "                    map[key] = list;",
        "                }",
        "                list.Add(text);",
        "            }",
        "            Dictionary<string, string[]> result = new Dictionary<string, string[]>();",
        "            foreach (KeyValuePair<string, List<string>> kv in map)",
        "                result[kv.Key] = kv.Value.ToArray();",
        "            return result;",
        "        }",
        "",
    ]
    for i, s in enumerate(zh_cs):
        parts.append("        private const string Zh%d =" % i)
        parts.append("            %s;" % s)
        parts.append("")
    for i, s in enumerate(en_cs):
        parts.append("        private const string En%d =" % i)
        parts.append("            %s;" % s)
        parts.append("")
    parts.append("    }")
    parts.append("}")
    parts.append("")
    text = "\n".join(parts)
    with open(OUT, "w", encoding="utf-8") as f:
        f.write(text)
    print("wrote", OUT, "air", air_n, "army", army_n, "bytes", os.path.getsize(OUT))


if __name__ == "__main__":
    main()
