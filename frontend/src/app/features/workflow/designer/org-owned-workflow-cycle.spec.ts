import { buildNWFMXml, type CanvasNode, type CanvasState } from './workflow-designer.component';

function node(partial: Partial<CanvasNode> & Pick<CanvasNode, 'id' | 'nodeKey' | 'type'>): CanvasNode {
  return {
    name: partial.name ?? partial.nodeKey,
    nameAr: '',
    actionKey: '',
    assignmentKey: '',
    assignmentGroupId: '',
    assignmentPurpose: '',
    configurationJson: '',
    x: 0,
    y: 0,
    outcomes: [],
    actions: [],
    ...partial,
  };
}

describe('org-owned workflow cycle (designer XML)', () => {
  const groupId = '11111111-1111-1111-1111-111111111111';

  function cycleCanvas(): CanvasState {
    return {
      nodes: [
        node({ id: 'n1', nodeKey: 'start', type: 'Start', name: 'Start' }),
        node({
          id: 'n2',
          nodeKey: 'review',
          type: 'UserTask',
          name: 'Privacy Review',
          assignmentGroupId: groupId,
          assignmentKey: 'PRIVACY_REVIEW',
        }),
        node({ id: 'n3', nodeKey: 'end', type: 'End', name: 'End' }),
      ],
      edges: [
        {
          id: 'e1',
          fromNodeId: 'n1',
          toNodeId: 'n2',
          transitionKey: 't1',
          labelEn: '',
          labelAr: '',
          descriptionEn: '',
          outcomeKey: '',
          conditionExpression: '',
          isDefault: false,
          priority: 0,
        },
        {
          id: 'e2',
          fromNodeId: 'n2',
          toNodeId: 'n3',
          transitionKey: 't2',
          labelEn: '',
          labelAr: '',
          descriptionEn: '',
          outcomeKey: '',
          conditionExpression: '',
          isDefault: false,
          priority: 0,
        },
      ],
      variables: [],
    };
  }

  it('emits the organization group id on the User Task', () => {
    const xml = buildNWFMXml(cycleCanvas());

    expect(xml).toContain(`assignmentGroupId="${groupId}"`);
    expect(xml).toContain('assignmentKey="PRIVACY_REVIEW"');
    expect(xml).toContain('assigneeType="AssignmentGroup"');
    expect(xml).toContain(`<AssignmentRule assigneeType="AssignmentGroup" assignmentGroupId="${groupId}" assignmentKey="PRIVACY_REVIEW" />`);
  });

  it('writes node coordinates so reload can restore the canvas', () => {
    const state = cycleCanvas();
    state.nodes[1] = { ...state.nodes[1], x: 420, y: 260 };
    const xml = buildNWFMXml(state);
    expect(xml).toContain('positionX="420"');
    expect(xml).toContain('positionY="260"');
  });

  it('does not emit an assignment rule when no group is selected', () => {
    const state = cycleCanvas();
    state.nodes[1] = node({
      id: 'n2',
      nodeKey: 'review',
      type: 'UserTask',
      name: 'Privacy Review',
    });

    const xml = buildNWFMXml(state);

    expect(xml).not.toContain('assignmentGroupId=');
    expect(xml).not.toContain('<AssignmentRule');
  });
});
