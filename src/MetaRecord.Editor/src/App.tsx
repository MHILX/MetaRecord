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

  return page === 'metadata'
    ? <MetadataViewerPage />
    : <WorkflowEditor />;
}

function getPageFromHash(): EditorPage {
  return window.location.hash === '#metadata' ? 'metadata' : 'workflow';
}