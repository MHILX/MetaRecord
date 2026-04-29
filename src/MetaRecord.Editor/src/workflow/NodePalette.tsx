import { Plus } from 'lucide-react';
import type { WorkflowDefinition, WorkflowNodeType } from '../api/types';
import { isNodeAllowedForWorkflow } from './workflowModel';

interface NodePaletteProps {
  workflow?: WorkflowDefinition | null;
  nodeTypes: WorkflowNodeType[];
  onAddNode: (nodeType: WorkflowNodeType) => void;
}

const categoryOrder = ['Flow', 'Action'];

export function NodePalette({ workflow, nodeTypes, onAddNode }: NodePaletteProps) {
  if (!workflow) {
    return (
      <section className="panel panel-compact">
        <h2>Node Palette</h2>
        <p className="muted">Create or open a workflow to add nodes.</p>
      </section>
    );
  }

  const availableNodes = nodeTypes.filter(nodeType => isNodeAllowedForWorkflow(nodeType, workflow.eventName));

  return (
    <section className="panel node-palette">
      <div className="panel-heading">
        <h2>Node Palette</h2>
        <span>{availableNodes.length}</span>
      </div>
      {categoryOrder.map(category => {
        const nodes = availableNodes.filter(nodeType => nodeType.category === category);
        if (nodes.length === 0)
          return null;

        return (
          <div className="palette-group" key={category}>
            <h3>{category}</h3>
            {nodes.map(nodeType => (
              <button className="palette-node" key={nodeType.type} type="button" onClick={() => onAddNode(nodeType)}>
                <span>
                  <strong>{nodeType.displayName}</strong>
                  <small>{nodeType.description}</small>
                </span>
                <Plus size={16} aria-hidden="true" />
              </button>
            ))}
          </div>
        );
      })}
    </section>
  );
}