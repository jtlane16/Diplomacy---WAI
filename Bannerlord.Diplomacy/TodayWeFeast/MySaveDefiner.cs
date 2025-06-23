using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;
using TodayWeFeast;

// Token: 0x0200003E RID: 62
public class MySaveDefiner : SaveableTypeDefiner
{
	// Token: 0x060001B8 RID: 440 RVA: 0x0000B467 File Offset: 0x00009667
	public MySaveDefiner() : base(2137782637)
	{
	}

	// Token: 0x060001B9 RID: 441 RVA: 0x0000B474 File Offset: 0x00009674
	protected override void DefineClassTypes()
	{
		base.AddClassDefinition(typeof(FeastObject), 2137782638);
		base.AddClassDefinition(typeof(FeastBehavior), 2137782639);
	}

	// Token: 0x060001BA RID: 442 RVA: 0x0000B48B File Offset: 0x0000968B
	protected override void DefineContainerDefinitions()
	{
		base.ConstructContainerDefinition(typeof(List<FeastObject>));
		base.ConstructContainerDefinition(typeof(Dictionary<Kingdom, double>));
	}
}