import { useEffect, useState } from 'react';
import { MetadataViewerPage } from './metadata/MetadataViewerPage';
import { WorkflowEditor } from './workflow/WorkflowEditor';

type EditorPage = 'workflow' | 'metadata';

export function App() {
  const [page, setPage] = useState<EditorPage>(() => getPageFromHash());

  useEffect(() => {
    function handleHashChange() {
      setPage(getPageFromHash());
    }

    window.addEventListener('hashchange', handleHashChange);
    return () => window.removeEventListener('hashchange', handleHashChange);
  }, []);

  function openWorkflowEditor() {
    window.location.hash = '#workflow';
  }

  function openMetadataViewer() {
    window.location.hash = '#metadata';
  }

  return page === 'metadata'
    ? <MetadataViewerPage onOpenWorkflowEditor={openWorkflowEditor} />
    : <WorkflowEditor onOpenMetadataViewer={openMetadataViewer} />;
}

function getPageFromHash(): EditorPage {
  return window.location.hash === '#metadata' ? 'metadata' : 'workflow';
}