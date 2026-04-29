import {
  Background,
  Controls,
  Handle,
  MarkerType,
  MiniMap,
  Position,
  ReactFlow,
  type Connection,
  type Edge,
  type EdgeChange,
  type Node,
  type NodeChange,
  type NodeProps
} from '@xyflow/react';
import type { WorkflowDefinition, WorkflowNode, WorkflowNodeType, WorkflowValidationIssue } from '../api/types';
import { getNodeIssues, getNodeType, updateNode, workflowHasEdge } from './workflowModel';

interface WorkflowCanvasProps {
  workflow: WorkflowDefinition;
  nodeTypes: WorkflowNodeType[];
  validationIssues: WorkflowValidationIssue[];
  selectedNodeId?: string | null;
  onSelectNode: (nodeId: string | null) => void;
  onWorkflowChange: (workflow: WorkflowDefinition) => void;
  onNotice: (message: string) => void;
}

interface WorkflowNodeData extends Record<string, unknown> {
  workflowNode: WorkflowNode;
  nodeType?: WorkflowNodeType;
  issueCount: number;
}

const reactFlowNodeTypes = {
  workflow: WorkflowGraphNode
};

export function WorkflowCanvas({
  workflow,
  nodeTypes,
  validationIssues,
  selectedNodeId,
  onSelectNode,
  onWorkflowChange,
  onNotice
}: WorkflowCanvasProps) {
  const flowNodes: Node<WorkflowNodeData>[] = workflow.nodes.map(node => {
    const nodeType = getNodeType(nodeTypes, node.type);
    return {
      id: node.id,
      type: 'workflow',
      position: node.position,
      selected: node.id === selectedNodeId,
      data: {
        workflowNode: node,
        nodeType,
        issueCount: getNodeIssues(validationIssues, node.id).length
      }
    };
  });

  const flowEdges: Edge[] = workflow.edges.map(edge => ({
    id: edge.id,
    source: edge.fromNodeId,
    target: edge.toNodeId,
    sourceHandle: edge.fromPort,
    targetHandle: edge.toPort,
    markerEnd: { type: MarkerType.ArrowClosed },
    className: 'workflow-edge'
  }));

  function handleNodesChange(changes: NodeChange[]) {
    let nextWorkflow = workflow;

    for (const change of changes) {
      if (change.type === 'position' && change.position) {
        nextWorkflow = updateNode(nextWorkflow, change.id, node => ({
          ...node,
          position: change.position!
        }));
      }

      if (change.type === 'remove') {
        const removedNode = nextWorkflow.nodes.find(node => node.id === change.id);
        const removedNodeType = removedNode ? getNodeType(nodeTypes, removedNode.type) : undefined;
        if (removedNodeType?.isTrigger) {
          onNotice('The trigger node cannot be removed from a workflow.');
          continue;
        }

        nextWorkflow = {
          ...nextWorkflow,
          nodes: nextWorkflow.nodes.filter(node => node.id !== change.id),
          edges: nextWorkflow.edges.filter(edge => edge.fromNodeId !== change.id && edge.toNodeId !== change.id)
        };
      }
    }

    onWorkflowChange(nextWorkflow);
  }

  function handleEdgesChange(changes: EdgeChange[]) {
    const removedEdgeIds = new Set(changes.filter(change => change.type === 'remove').map(change => change.id));
    if (removedEdgeIds.size === 0)
      return;

    onWorkflowChange({
      ...workflow,
      edges: workflow.edges.filter(edge => !removedEdgeIds.has(edge.id))
    });
  }

  function handleConnect(connection: Connection) {
    if (!connection.source || !connection.target || !connection.sourceHandle || !connection.targetHandle)
      return;

    if (!isCompatibleConnection(connection)) {
      onNotice('That connection does not match the node port schema.');
      return;
    }

    const edge = {
      fromNodeId: connection.source,
      fromPort: connection.sourceHandle,
      toNodeId: connection.target,
      toPort: connection.targetHandle
    };

    if (workflowHasEdge(workflow, edge)) {
      onNotice('That connection already exists.');
      return;
    }

    onWorkflowChange({
      ...workflow,
      edges: [
        ...workflow.edges,
        { id: `edge-${crypto.randomUUID().slice(0, 8)}`, ...edge }
      ]
    });
  }

  function isCompatibleConnection(connection: Connection) {
    const sourceNode = workflow.nodes.find(node => node.id === connection.source);
    const targetNode = workflow.nodes.find(node => node.id === connection.target);
    if (!sourceNode || !targetNode || sourceNode.id === targetNode.id)
      return false;

    const sourceType = getNodeType(nodeTypes, sourceNode.type);
    const targetType = getNodeType(nodeTypes, targetNode.type);
    if (!sourceType || !targetType)
      return false;

    const hasSourcePort = sourceType.outputPorts.some(port => port.name === connection.sourceHandle);
    const hasTargetPort = targetType.inputPorts.some(port => port.name === connection.targetHandle);
    return hasSourcePort && hasTargetPort;
  }

  return (
    <div className="workflow-canvas-shell">
      <ReactFlow
        nodes={flowNodes}
        edges={flowEdges}
        nodeTypes={reactFlowNodeTypes}
        onNodesChange={handleNodesChange}
        onEdgesChange={handleEdgesChange}
        onConnect={handleConnect}
        onNodeClick={(_, node) => onSelectNode(node.id)}
        onPaneClick={() => onSelectNode(null)}
        fitView
      >
        <Background color="#d8dee6" gap={18} />
        <Controls showInteractive={false} />
        <MiniMap pannable zoomable nodeStrokeWidth={3} />
      </ReactFlow>
    </div>
  );
}

function WorkflowGraphNode(props: NodeProps) {
  const data = props.data as WorkflowNodeData;
  const node = data.workflowNode;
  const nodeType = data.nodeType;
  const inputPorts = nodeType?.inputPorts ?? [];
  const outputPorts = nodeType?.outputPorts ?? [];

  return (
    <div className={`graph-node graph-node-${nodeType?.category?.toLowerCase() ?? 'unknown'} ${data.issueCount > 0 ? 'graph-node-invalid' : ''}`}>
      {inputPorts.map((port, index) => (
        <Handle
          key={port.name}
          id={port.name}
          type="target"
          position={Position.Left}
          style={{ top: portOffset(index, inputPorts.length) }}
          title={port.displayName}
        />
      ))}
      <div className="graph-node-kind">{nodeType?.category ?? 'Unknown'}</div>
      <div className="graph-node-title">{node.label || nodeType?.displayName || node.type}</div>
      <div className="graph-node-type">{node.type}</div>
      {data.issueCount > 0 && <div className="graph-node-issue">{data.issueCount}</div>}
      {outputPorts.map((port, index) => (
        <Handle
          key={port.name}
          id={port.name}
          type="source"
          position={Position.Right}
          style={{ top: portOffset(index, outputPorts.length) }}
          title={port.displayName}
        />
      ))}
    </div>
  );
}

function portOffset(index: number, total: number) {
  if (total <= 1)
    return '50%';

  return `${((index + 1) / (total + 1)) * 100}%`;
}