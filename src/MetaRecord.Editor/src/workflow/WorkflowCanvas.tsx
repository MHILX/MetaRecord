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
import { Trash2 } from 'lucide-react';
import type { MouseEvent } from 'react';
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
  onDeleteNode: (nodeId: string) => void;
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
        issueCount: getNodeIssues(validationIssues, node.id).length,
        onDeleteNode: deleteNode
      }
    };
  });

  function removeNodeFromWorkflow(currentWorkflow: WorkflowDefinition, nodeId: string): WorkflowDefinition | null {
    const removedNode = currentWorkflow.nodes.find(node => node.id === nodeId);
    const removedNodeType = removedNode ? getNodeType(nodeTypes, removedNode.type) : undefined;

    if (removedNodeType?.isTrigger) {
      onNotice('The trigger node cannot be removed from a workflow.');
      return null;
    }

    return {
      ...currentWorkflow,
      nodes: currentWorkflow.nodes.filter(node => node.id !== nodeId),
      edges: currentWorkflow.edges.filter(edge => edge.fromNodeId !== nodeId && edge.toNodeId !== nodeId)
    };
  }

  function deleteNode(nodeId: string) {
    const nextWorkflow = removeNodeFromWorkflow(workflow, nodeId);
    if (!nextWorkflow)
      return;

    onWorkflowChange(nextWorkflow);
    onSelectNode(null);
  }

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
    let didRemoveNode = false;

    for (const change of changes) {
      if (change.type === 'position' && change.position) {
        nextWorkflow = updateNode(nextWorkflow, change.id, node => ({
          ...node,
          position: change.position!
        }));
      }

      if (change.type === 'remove') {
        const removedNodeWorkflow = removeNodeFromWorkflow(nextWorkflow, change.id);
        if (!removedNodeWorkflow)
          continue;

        nextWorkflow = removedNodeWorkflow;
        didRemoveNode = true;
      }
    }

    onWorkflowChange(nextWorkflow);
    if (didRemoveNode)
      onSelectNode(null);
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
  const isTriggerNode = Boolean(nodeType?.isTrigger);

  function handleDeleteNode(event: MouseEvent<HTMLButtonElement>) {
    event.stopPropagation();
    data.onDeleteNode(node.id);
  }

  return (
    <div className={`graph-node graph-node-${nodeType?.category?.toLowerCase() ?? 'unknown'} ${data.issueCount > 0 ? 'graph-node-invalid' : ''}`}>
      <button
        className="graph-node-delete-button"
        type="button"
        onMouseDown={event => event.stopPropagation()}
        onClick={handleDeleteNode}
        disabled={isTriggerNode}
        title={isTriggerNode ? 'Trigger nodes cannot be deleted' : 'Delete node'}
        aria-label={isTriggerNode ? 'Trigger nodes cannot be deleted' : `Delete ${node.label || nodeType?.displayName || node.type}`}
      >
        <Trash2 size={12} aria-hidden="true" />
      </button>
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